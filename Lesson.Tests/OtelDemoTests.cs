using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Lesson.Data;

namespace Lesson.Tests;

/// <summary>
/// Lesson 15-C — OpenTelemetry and health check tests.
///
/// We verify:
///   1. Custom-span endpoints return correct HTTP responses.
///   2. GET /health returns 200 (Healthy) when the DB is reachable.
///
/// Testing that ActivitySource actually emitted spans would require a custom
/// in-process exporter; for a teaching context, HTTP-level verification is
/// sufficient to confirm the instrumented paths are exercised without error.
/// </summary>
public class OtelDemoTests : IClassFixture<OtelTestFactory>
{
    private readonly OtelTestFactory _factory;
    public OtelDemoTests(OtelTestFactory factory) => _factory = factory;

    private HttpClient NewClient() => _factory.CreateClient(
        new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private async Task<(int Id1, int Id2)> SeedTwoAccountsAsync()
    {
        var client = NewClient();
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var r1 = await client.PostAsJsonAsync("/accounts",
            new { accountNumber = $"OTL-A-{suffix}", ownerName = "Alice", accountType = "Savings", initialBalance = 1000m });
        var r2 = await client.PostAsJsonAsync("/accounts",
            new { accountNumber = $"OTL-B-{suffix}", ownerName = "Bob",   accountType = "Savings", initialBalance = 500m });
        var a1 = await r1.Content.ReadFromJsonAsync<AccountSummary>();
        var a2 = await r2.Content.ReadFromJsonAsync<AccountSummary>();
        return (a1!.Id, a2!.Id);
    }

    // ── /health returns 200 ───────────────────────────────────────────────────

    [Fact]
    public async Task Health_Returns200()
    {
        var resp = await NewClient().GetAsync("/health");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── GET with custom span — returns 200 ───────────────────────────────────

    [Fact]
    public async Task GetAccount_WithCustomSpan_Returns200()
    {
        var (id1, _) = await SeedTwoAccountsAsync();
        var resp     = await NewClient().GetAsync($"/otel-demo/accounts/{id1}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── GET missing account — span marked Error, returns 404 ─────────────────

    [Fact]
    public async Task GetAccount_NotFound_Returns404()
    {
        var resp = await NewClient().GetAsync("/otel-demo/accounts/999999");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Transfer happy path — multi-span trace, returns 200 ──────────────────

    [Fact]
    public async Task Transfer_HappyPath_Returns200()
    {
        var (id1, id2) = await SeedTwoAccountsAsync();
        var resp = await NewClient().PostAsJsonAsync("/otel-demo/transfer",
            new { from = id1, to = id2, amount = 100m });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── Transfer — insufficient funds ────────────────────────────────────────

    [Fact]
    public async Task Transfer_InsufficientFunds_Returns400()
    {
        var (id1, id2) = await SeedTwoAccountsAsync();
        var resp = await NewClient().PostAsJsonAsync("/otel-demo/transfer",
            new { from = id1, to = id2, amount = 9999m });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}

public class OtelTestFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection;
    public OtelTestFactory()
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
