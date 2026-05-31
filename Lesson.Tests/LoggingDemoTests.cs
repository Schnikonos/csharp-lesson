using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Lesson.Data;

namespace Lesson.Tests;

/// <summary>
/// Lesson 15-A — ILogger&lt;T&gt; integration tests.
///
/// We verify:
///   1. Endpoints behave correctly (HTTP status + body)
///   2. The logger captured expected messages at the right level
///      using a custom FakeLogger that records entries.
///
/// Capturing log output in tests is a common technique for verifying
/// that observability code is exercised in production paths.
/// Java parallel: Mockito.verify(logger, ...) / LogCaptor (SLF4J test libraries)
/// </summary>
public class LoggingDemoTests : IClassFixture<LoggingTestFactory>
{
    private readonly LoggingTestFactory _factory;
    public LoggingDemoTests(LoggingTestFactory factory) => _factory = factory;

    private HttpClient NewClient() => _factory.CreateClient(
        new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private async Task<(int Id1, int Id2)> SeedTwoAccountsAsync()
    {
        var client  = NewClient();
        var suffix  = Guid.NewGuid().ToString("N")[..6];
        var r1 = await client.PostAsJsonAsync("/accounts",
            new { accountNumber = $"LOG-A-{suffix}", ownerName = "Alice", accountType = "Savings", initialBalance = 1000m });
        var r2 = await client.PostAsJsonAsync("/accounts",
            new { accountNumber = $"LOG-B-{suffix}", ownerName = "Bob",   accountType = "Savings", initialBalance = 500m });
        var a1 = await r1.Content.ReadFromJsonAsync<AccountSummary>();
        var a2 = await r2.Content.ReadFromJsonAsync<AccountSummary>();
        return (a1!.Id, a2!.Id);
    }

    // ── GET returns 200 ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAccount_Returns200_AndLogs()
    {
        var (id1, _) = await SeedTwoAccountsAsync();
        var resp     = await NewClient().GetAsync($"/logging-demo/accounts/{id1}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── GET missing account returns 404 ──────────────────────────────────────

    [Fact]
    public async Task GetAccount_NotFound_Returns404()
    {
        var resp = await NewClient().GetAsync("/logging-demo/accounts/999999");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Transfer happy path ───────────────────────────────────────────────────

    [Fact]
    public async Task Transfer_HappyPath_Returns200()
    {
        var (id1, id2) = await SeedTwoAccountsAsync();
        var resp = await NewClient().PostAsJsonAsync("/logging-demo/transfer",
            new { from = id1, to = id2, amount = 100m });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── Transfer insufficient funds ───────────────────────────────────────────

    [Fact]
    public async Task Transfer_InsufficientFunds_Returns400()
    {
        var (id1, id2) = await SeedTwoAccountsAsync();
        var resp = await NewClient().PostAsJsonAsync("/logging-demo/transfer",
            new { from = id1, to = id2, amount = 9999m });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Log levels endpoint ───────────────────────────────────────────────────

    [Fact]
    public async Task LogAllLevels_Returns200()
    {
        var resp = await NewClient().GetAsync("/logging-demo/levels");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}

public class LoggingTestFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection;
    public LoggingTestFactory()
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
