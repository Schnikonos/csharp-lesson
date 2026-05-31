using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Lesson.Data;

namespace Lesson.Tests;

/// <summary>
/// Lesson 14-A — IMemoryCache integration tests.
///
/// Tests verify: cache hit returns same data, cache is invalidated on write.
/// </summary>
public class MemoryCacheTests : IClassFixture<CacheTestFactory>
{
    private readonly CacheTestFactory _factory;
    public MemoryCacheTests(CacheTestFactory factory) => _factory = factory;

    private HttpClient NewClient() => _factory.CreateClient(
        new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<int> CreateAccountAsync(string number)
    {
        var client = NewClient();
        var resp   = await client.PostAsJsonAsync("/cache-demo/accounts",
            new { accountNumber = number, ownerName = "Tester", accountType = "Savings", initialBalance = 0m });
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<AccountSummary>();
        return body!.Id;
    }

    // ── Cache hit ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAccount_SecondRequest_ReturnsCachedData()
    {
        var id     = await CreateAccountAsync("CACHE-001");
        var client = NewClient();

        var r1 = await client.GetAsync($"/cache-demo/accounts/{id}");
        var r2 = await client.GetAsync($"/cache-demo/accounts/{id}");

        r1.StatusCode.Should().Be(HttpStatusCode.OK);
        r2.StatusCode.Should().Be(HttpStatusCode.OK);

        var a1 = await r1.Content.ReadFromJsonAsync<AccountSummary>();
        var a2 = await r2.Content.ReadFromJsonAsync<AccountSummary>();
        a1!.Id.Should().Be(a2!.Id);
    }

    // ── Cache invalidation on write ───────────────────────────────────────────

    [Fact]
    public async Task CreateAccount_InvalidatesAllAccountsCache()
    {
        var client = NewClient();

        // Warm the list cache
        var list1 = await client.GetAsync("/cache-demo/accounts");
        list1.StatusCode.Should().Be(HttpStatusCode.OK);

        // Create a new account (should evict the list cache)
        await CreateAccountAsync("CACHE-INV");

        // Fetch the list again — should contain the new entry
        var list2 = await client.GetAsync("/cache-demo/accounts");
        var accounts = await list2.Content.ReadFromJsonAsync<List<AccountSummary>>();
        accounts.Should().Contain(a => a.AccountNumber == "CACHE-INV");
    }

    // ── Manual eviction ───────────────────────────────────────────────────────

    [Fact]
    public async Task EvictEndpoint_ClearsCacheEntry()
    {
        var id     = await CreateAccountAsync("CACHE-EV1");
        var client = NewClient();

        await client.GetAsync($"/cache-demo/accounts/{id}");  // warm cache

        var evict = await client.DeleteAsync($"/cache-demo/accounts/{id}/cache");
        evict.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // After eviction the entry is gone — next GET hits DB again
        var after = await client.GetAsync($"/cache-demo/accounts/{id}");
        after.StatusCode.Should().Be(HttpStatusCode.OK);   // DB still has it
    }

    // ── Not found ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAccount_NotFound_Returns404()
    {
        var resp = await NewClient().GetAsync("/cache-demo/accounts/999999");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}

internal record AccountSummary(int Id, string AccountNumber, string OwnerName,
    string AccountType, decimal Balance, bool IsActive);

public class CacheTestFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection;
    public CacheTestFactory()
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
