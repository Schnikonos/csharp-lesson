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
/// Lesson 15-B — Serilog integration tests.
///
/// We verify:
///   1. The Serilog-backed endpoints return correct HTTP responses.
///   2. Correlation ID forwarded via X-Correlation-ID header is reflected in the
///      response (or at least accepted without error).
///   3. The enrichment endpoint returns 200 with the expected JSON shape.
///
/// Note: verifying that log entries actually contain the CorrelationId property
/// requires a custom Serilog sink that captures events; for a teaching context
/// we keep tests HTTP-level and focus on observable behavior.
/// </summary>
public class SerilogDemoTests : IClassFixture<SerilogTestFactory>
{
    private readonly SerilogTestFactory _factory;
    public SerilogDemoTests(SerilogTestFactory factory) => _factory = factory;

    private HttpClient NewClient() => _factory.CreateClient(
        new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private async Task<int> SeedAccountAsync()
    {
        var client = NewClient();
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var resp = await client.PostAsJsonAsync("/accounts",
            new { accountNumber = $"SLG-{suffix}", ownerName = "Eve", accountType = "Checking", initialBalance = 800m });
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<AccountSummary>();
        return body!.Id;
    }

    // ── Correlation ID is forwarded without error ─────────────────────────────
    [Fact]
    public async Task GetAccount_WithCorrelationHeader_Returns200()
    {
        var id     = await SeedAccountAsync();
        var client = NewClient();
        client.DefaultRequestHeaders.Add("X-Correlation-ID", "test-cid-123");
        var resp = await client.GetAsync($"/serilog-demo/accounts/{id}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── Missing account returns 404 ───────────────────────────────────────────
    [Fact]
    public async Task GetAccount_NotFound_Returns404()
    {
        var resp = await NewClient().GetAsync("/serilog-demo/accounts/999999");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Request without X-Correlation-ID still works (fallback guid) ──────────
    [Fact]
    public async Task GetAccount_WithoutCorrelationHeader_Returns200()
    {
        var id   = await SeedAccountAsync();
        var resp = await NewClient().GetAsync($"/serilog-demo/accounts/{id}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── Enrichment endpoint returns 200 + correlationId in body ──────────────
    [Fact]
    public async Task Enrich_Returns200_WithCorrelationId()
    {
        var client = NewClient();
        client.DefaultRequestHeaders.Add("X-Correlation-ID", "enrich-cid");
        var resp = await client.GetAsync("/serilog-demo/enrich");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<EnrichResponse>();
        body!.CorrelationId.Should().Be("enrich-cid");
    }
}

public record EnrichResponse(string CorrelationId, string Message);

public class SerilogTestFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection;
    public SerilogTestFactory()
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
