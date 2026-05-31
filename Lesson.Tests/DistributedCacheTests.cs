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
/// Lesson 14-B — IDistributedCache integration tests.
///
/// Uses the in-memory distributed cache (registered via AddDistributedMemoryCache
/// in Development mode). This isolates tests from needing a real Redis instance
/// while still exercising the full IDistributedCache code path.
/// </summary>
public class DistributedCacheTests : IClassFixture<DistCacheTestFactory>
{
    private readonly DistCacheTestFactory _factory;
    public DistributedCacheTests(DistCacheTestFactory factory) => _factory = factory;

    private HttpClient NewClient() => _factory.CreateClient(
        new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private async Task<(int Id, string Number)> CreateAsync(string number)
    {
        var resp = await NewClient().PostAsJsonAsync("/distributed-cache/accounts",
            new { accountNumber = number, ownerName = "DC Tester", accountType = "Savings", initialBalance = 0m });
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<AccountSummary>();
        return (body!.Id, body.AccountNumber);
    }

    // ── First GET hits DB, second returns from cache ──────────────────────────

    [Fact]
    public async Task GetAccount_SecondCall_ComesFromCache()
    {
        var (id, _) = await CreateAsync("DC-001");
        var client  = NewClient();

        var r1 = await client.GetAsync($"/distributed-cache/accounts/{id}");
        var r2 = await client.GetAsync($"/distributed-cache/accounts/{id}");

        r1.StatusCode.Should().Be(HttpStatusCode.OK);
        r2.StatusCode.Should().Be(HttpStatusCode.OK);

        // Second response should be tagged "cache"
        var body2 = await r2.Content.ReadFromJsonAsync<CacheSourceWrapper<AccountSummary>>();
        body2!.Source.Should().Be("cache");
    }

    // ── Cache miss (first call) is tagged "db" ───────────────────────────────

    [Fact]
    public async Task GetAccount_FirstCall_ComesFromDb()
    {
        var (id, _) = await CreateAsync("DC-002");
        var client  = NewClient();

        var r1   = await client.GetAsync($"/distributed-cache/accounts/{id}");
        var body = await r1.Content.ReadFromJsonAsync<CacheSourceWrapper<AccountSummary>>();
        body!.Source.Should().Be("db");
    }

    // ── Evict then GET again — should hit DB ──────────────────────────────────

    [Fact]
    public async Task EvictThenGet_HitsDbAgain()
    {
        var (id, _) = await CreateAsync("DC-003");
        var client  = NewClient();

        await client.GetAsync($"/distributed-cache/accounts/{id}");  // warm cache

        var evict = await client.DeleteAsync($"/distributed-cache/accounts/{id}/cache");
        evict.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var r = await client.GetAsync($"/distributed-cache/accounts/{id}");
        var body = await r.Content.ReadFromJsonAsync<CacheSourceWrapper<AccountSummary>>();
        body!.Source.Should().Be("db");
    }

    // ── Not found ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAccount_NotFound_Returns404()
    {
        var resp = await NewClient().GetAsync("/distributed-cache/accounts/999999");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}

internal record CacheSourceWrapper<T>(string Source, T? Account, T? Cached);

public class DistCacheTestFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection;
    public DistCacheTestFactory()
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
