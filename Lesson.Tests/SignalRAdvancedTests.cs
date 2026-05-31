using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Lesson.Data;
using Lesson.Hubs;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lesson.Tests;

/// <summary>
/// Lesson 24-B — Advanced SignalR: groups, IUserIdProvider, authorization.
///
/// These tests exercise:
///   1. Multiple independent clients each subscribed to a different account group
///      — cross-group isolation
///   2. Multiple clients subscribed to the SAME group all receive the broadcast
///   3. The custom JwtUserIdProvider wiring (verified via DI)
///
/// Java parallel:
///   Spring WebSocket SimpUserRegistry / @SendToUser  →  Clients.User(userId)
///   @PreAuthorize on @MessageMapping                  →  [Authorize] on hub methods
/// </summary>
public class SignalRAdvancedTests : IClassFixture<SignalRAdvancedFactory>
{
    private readonly SignalRAdvancedFactory _factory;
    public SignalRAdvancedTests(SignalRAdvancedFactory factory) => _factory = factory;

    private HubConnection BuildConnection() =>
        new HubConnectionBuilder()
            .WithUrl(_factory.BaseUrl + "/hubs/banking",
                opts => opts.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler())
            .Build();

    private async Task<int> CreateAccountAsync(string number, decimal balance = 500m)
    {
        var r = await _factory.CreateClient().PostAsJsonAsync("/result/accounts",
            new { accountNumber = number, ownerName = "Test", initialBalance = balance });
        var body = await r.Content.ReadFromJsonAsync<IdHolderAdv>();
        return body!.Id;
    }

    // Two clients on different groups: only the subscribed group receives the event
    [Fact]
    public async Task TwoGroups_EachClientReceivesOnlyOwnEvents()
    {
        var id1 = await CreateAccountAsync("ADV-001");
        var id2 = await CreateAccountAsync("ADV-002");

        var conn1 = BuildConnection();
        var conn2 = BuildConnection();

        int received1 = 0, received2 = 0;
        conn1.On<BalanceChangedEvent>("ReceiveBalanceChanged", _ => received1++);
        conn2.On<BalanceChangedEvent>("ReceiveBalanceChanged", _ => received2++);

        await conn1.StartAsync();
        await conn2.StartAsync();
        await conn1.InvokeAsync("Subscribe", id1);
        await conn2.InvokeAsync("Subscribe", id2);

        // Deposit to account 1 only
        await _factory.CreateClient().PostAsJsonAsync(
            $"/signalr/accounts/{id1}/deposit", new { amount = 50m });

        await Task.Delay(300);

        received1.Should().Be(1, "conn1 is subscribed to account {0}", id1);
        received2.Should().Be(0, "conn2 is subscribed to a different account");

        await conn1.StopAsync();
        await conn2.StopAsync();
    }

    // Two clients on the SAME group both receive the broadcast
    [Fact]
    public async Task SameGroup_BothClientsReceiveEvent()
    {
        var id = await CreateAccountAsync("ADV-003");

        var conn1 = BuildConnection();
        var conn2 = BuildConnection();

        var tcs1 = new TaskCompletionSource<BalanceChangedEvent>();
        var tcs2 = new TaskCompletionSource<BalanceChangedEvent>();

        conn1.On<BalanceChangedEvent>("ReceiveBalanceChanged", evt => tcs1.TrySetResult(evt));
        conn2.On<BalanceChangedEvent>("ReceiveBalanceChanged", evt => tcs2.TrySetResult(evt));

        await conn1.StartAsync();
        await conn2.StartAsync();
        await conn1.InvokeAsync("Subscribe", id);
        await conn2.InvokeAsync("Subscribe", id);

        await _factory.CreateClient().PostAsJsonAsync(
            $"/signalr/accounts/{id}/deposit", new { amount = 25m });

        var timeout = TimeSpan.FromSeconds(5);
        await Task.WhenAll(
            tcs1.Task.WaitAsync(timeout),
            tcs2.Task.WaitAsync(timeout));

        tcs1.Task.Result.AccountId.Should().Be(id);
        tcs2.Task.Result.AccountId.Should().Be(id);

        await conn1.StopAsync();
        await conn2.StopAsync();
    }

    // IUserIdProvider is correctly registered in DI
    [Fact]
    public void JwtUserIdProvider_IsRegistered()
    {
        using var scope = _factory.Services.CreateScope();
        var provider = scope.ServiceProvider
            .GetRequiredService<Microsoft.AspNetCore.SignalR.IUserIdProvider>();
        provider.Should().BeOfType<JwtUserIdProvider>();
    }

    // v2 hub (BankingHubV2) endpoint responds (negotiate returns 200)
    [Fact]
    public async Task BankingHubV2_NegotiateReturns200()
    {
        var r = await _factory.CreateClient().PostAsync("/hubs/banking/v2/negotiate?negotiateVersion=1", null);
        r.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}

file record IdHolderAdv(int Id);

public class SignalRAdvancedFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection;
    public SignalRAdvancedFactory() { _connection = new("DataSource=:memory:"); _connection.Open(); }
    public string BaseUrl => "http://localhost";
    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder) =>
        builder.ConfigureServices(services =>
        {
            var d = services.SingleOrDefault(s => s.ServiceType == typeof(DbContextOptions<BankingDbContext>));
            if (d is not null) services.Remove(d);
            services.AddDbContext<BankingDbContext>(o => o.UseSqlite(_connection));
            var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            scope.ServiceProvider.GetRequiredService<BankingDbContext>().Database.Migrate();
        });
    protected override void Dispose(bool d) { base.Dispose(d); if (d) _connection.Dispose(); }
}
