using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Lesson.Messaging.Consumers;
using Lesson.Messaging.Contracts;
using Lesson.Messaging.Events;
using MassTransit;
using MassTransit.Testing;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Lesson.Data;

namespace Lesson.Tests;

/// <summary>
/// Lesson 17-B — Request/Response pattern + retry tests.
/// </summary>
public class MessagingIntermediateTests : IClassFixture<MessagingBFactory>
{
    private readonly MessagingBFactory _factory;
    public MessagingIntermediateTests(MessagingBFactory factory) => _factory = factory;

    private HttpClient NewClient() => _factory.CreateClient(
        new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private async Task<int> SeedAccountAsync(decimal balance = 3000m)
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var resp = await NewClient().PostAsJsonAsync("/accounts",
            new { accountNumber = $"MSG-B-{suffix}", ownerName = "Msg User",
                  accountType = "Savings", initialBalance = balance });
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<AccountSummary>();
        return body!.Id;
    }

    // GET /messaging/balance/{id} returns balance for known account
    [Fact]
    public async Task GetBalance_KnownAccount_Returns200WithBalance()
    {
        var id   = await SeedAccountAsync(5000m);
        var resp = await NewClient().GetAsync($"/messaging/balance/{id}");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<BalanceResult>();
        body!.Balance.Should().Be(5000m);
        body.Found.Should().BeTrue();
    }

    // GET /messaging/balance/{id} returns 404 for missing account
    [Fact]
    public async Task GetBalance_UnknownAccount_Returns404()
    {
        var resp = await NewClient().GetAsync("/messaging/balance/999111");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // Consumer harness records the request
    [Fact]
    public async Task GetBalance_ConsumerIsInvoked()
    {
        var id = await SeedAccountAsync();
        await NewClient().GetAsync($"/messaging/balance/{id}");

        await Task.Delay(200);

        using var scope = _factory.Services.CreateScope();
        var harness = scope.ServiceProvider.GetRequiredService<ITestHarness>();
        var ch = harness.GetConsumerHarness<GetAccountBalanceConsumer>();
        (await ch.Consumed.Any<GetAccountBalanceRequest>()).Should().BeTrue();
    }

    // Publish still works (regression)
    [Fact]
    public async Task Publish_StillWorks_After17B()
    {
        var resp = await NewClient().PostAsJsonAsync("/messaging/account-created",
            new { accountId = 99, accountNumber = "MSG-B-REG", ownerName = "Regress", initialBalance = 1m });
        resp.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }
}

public record BalanceResult(int AccountId, decimal Balance, bool Found);

public class MessagingBFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection;
    public MessagingBFactory()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }
    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder) =>
        builder.ConfigureServices(services =>
        {
            var d = services.SingleOrDefault(s => s.ServiceType == typeof(DbContextOptions<BankingDbContext>));
            if (d is not null) services.Remove(d);
            services.AddDbContext<BankingDbContext>(o => o.UseSqlite(_connection));
            services.AddMassTransitTestHarness(x => x.AddConsumers(typeof(Program).Assembly));
            var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            scope.ServiceProvider.GetRequiredService<BankingDbContext>().Database.Migrate();
        });
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _connection.Dispose();
    }
}
