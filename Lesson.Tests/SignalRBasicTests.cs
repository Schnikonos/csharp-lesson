using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Lesson.Data;
using Lesson.Hubs;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lesson.Tests;

/// <summary>
/// Lesson 24-A — SignalR Hub tests.
///
/// HubConnection backed by WebApplicationFactory's in-process TestServer;
/// we subscribe to a group, trigger a deposit via HTTP, and assert the
/// ReceiveBalanceChanged message arrives.
///
/// Java parallel:
///   Spring Boot WebSocket tests using StompSession + StompFrameHandler
///   MessagingTemplate.convertAndSend(...) → stomp.subscribe(...)
/// </summary>
public class SignalRBasicTests : IClassFixture<SignalRFactory>
{
    private readonly SignalRFactory _factory;
    public SignalRBasicTests(SignalRFactory factory) => _factory = factory;

    // ── helpers ──────────────────────────────────────────────────────────────

    private HubConnection BuildConnection() =>
        new HubConnectionBuilder()
            .WithUrl(_factory.BaseUrl + "/hubs/banking", opts =>
                opts.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler())
            .Build();

    private async Task<int> CreateAccountAsync(string number, decimal balance = 500m)
    {
        var r = await _factory.CreateClient().PostAsJsonAsync("/result/accounts",
            new { accountNumber = number, ownerName = "Test", initialBalance = balance });
        var body = await r.Content.ReadFromJsonAsync<IdHolder>();
        return body!.Id;
    }

    // ── tests ─────────────────────────────────────────────────────────────────

    // Deposit broadcasts BalanceChangedEvent to subscribed client
    [Fact]
    public async Task Deposit_NotifiesSubscribedClient()
    {
        var id         = await CreateAccountAsync("SIG-001");
        var connection = BuildConnection();
        var received   = new TaskCompletionSource<BalanceChangedEvent>();

        connection.On<BalanceChangedEvent>("ReceiveBalanceChanged", evt => received.TrySetResult(evt));

        await connection.StartAsync();
        await connection.InvokeAsync("Subscribe", id);

        await _factory.CreateClient().PostAsJsonAsync(
            $"/signalr/accounts/{id}/deposit", new { amount = 100m });

        var evt = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        evt.AccountId.Should().Be(id);
        evt.NewBalance.Should().Be(600m);
        evt.Reason.Should().Be("deposit");

        await connection.StopAsync();
    }

    // Client not subscribed to the group must NOT receive events
    [Fact]
    public async Task Deposit_DoesNotNotifyUnsubscribedClient()
    {
        var id         = await CreateAccountAsync("SIG-002");
        var connection = BuildConnection();
        bool notified  = false;
        connection.On<BalanceChangedEvent>("ReceiveBalanceChanged", _ => notified = true);

        await connection.StartAsync();
        // intentionally NOT calling Subscribe(id)

        await _factory.CreateClient().PostAsJsonAsync(
            $"/signalr/accounts/{id}/deposit", new { amount = 50m });

        await Task.Delay(300);   // brief wait — should receive nothing
        notified.Should().BeFalse();

        await connection.StopAsync();
    }

    // After Unsubscribe the client stops receiving events for that account
    [Fact]
    public async Task AfterUnsubscribe_NoMoreEvents()
    {
        var id         = await CreateAccountAsync("SIG-003");
        var connection = BuildConnection();
        int count      = 0;
        connection.On<BalanceChangedEvent>("ReceiveBalanceChanged", _ => count++);

        await connection.StartAsync();
        await connection.InvokeAsync("Subscribe", id);
        await connection.InvokeAsync("Unsubscribe", id);

        await _factory.CreateClient().PostAsJsonAsync(
            $"/signalr/accounts/{id}/deposit", new { amount = 10m });

        await Task.Delay(300);
        count.Should().Be(0);

        await connection.StopAsync();
    }

    // Deposit to unknown account returns 404 (no push attempted)
    [Fact]
    public async Task Deposit_UnknownAccount_Returns404()
    {
        var r = await _factory.CreateClient().PostAsJsonAsync(
            "/signalr/accounts/99999/deposit", new { amount = 10m });
        r.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}

file record IdHolder(int Id);

public class SignalRFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection;

    public SignalRFactory()
    {
        _connection = new("DataSource=:memory:");
        _connection.Open();
    }

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

    protected override void Dispose(bool d)
    {
        base.Dispose(d);
        if (d) _connection.Dispose();
    }
}
