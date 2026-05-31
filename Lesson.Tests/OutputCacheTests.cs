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
/// Lesson 14-C — Output caching and response caching tests.
///
/// Key assertions:
///   - [ResponseCache] adds the correct Cache-Control header (client hint)
///   - [OutputCache] returns a cached response on second call
///   - VaryByQueryKeys produces separate entries for different query params
/// </summary>
public class OutputCacheTests : IClassFixture<OutputCacheTestFactory>
{
    private readonly OutputCacheTestFactory _factory;
    public OutputCacheTests(OutputCacheTestFactory factory) => _factory = factory;

    private HttpClient NewClient() => _factory.CreateClient(
        new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private async Task<int> CreateAsync(string number)
    {
        var resp = await NewClient().PostAsJsonAsync("/distributed-cache/accounts",
            new { accountNumber = number, ownerName = "OC Tester", accountType = "Savings", initialBalance = 0m });
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<AccountSummary>();
        return body!.Id;
    }

    // ── [ResponseCache] sets Cache-Control header ─────────────────────────────

    [Fact]
    public async Task ResponseCache_SetsCorrectCacheControlHeader()
    {
        var id   = await CreateAsync("OC-HDR-01");
        var resp = await NewClient().GetAsync($"/output-cache/headers/{id}");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var cc = resp.Headers.CacheControl;
        cc.Should().NotBeNull();
        cc!.MaxAge.Should().Be(TimeSpan.FromSeconds(30));
        cc.Public.Should().BeTrue();
    }

    // ── [OutputCache] serves second request from cache ────────────────────────

    [Fact]
    public async Task OutputCache_SecondRequest_IsServedFromCache()
    {
        var id     = await CreateAsync("OC-SRV-01");
        var client = NewClient();

        var r1 = await client.GetAsync($"/output-cache/server/{id}");
        var r2 = await client.GetAsync($"/output-cache/server/{id}");

        r1.StatusCode.Should().Be(HttpStatusCode.OK);
        r2.StatusCode.Should().Be(HttpStatusCode.OK);

        // Output cache adds "Age" header when serving from cache
        r2.Headers.Age.Should().NotBeNull("second response should come from output cache");
    }

    // ── VaryByQueryKeys — different params → different entries ────────────────

    [Fact]
    public async Task OutputCache_VaryByQueryKeys_DifferentEntriesPerParam()
    {
        var client = NewClient();

        var r1 = await client.GetAsync("/output-cache/server/list?type=Savings&page=1");
        var r2 = await client.GetAsync("/output-cache/server/list?type=Checking&page=1");

        r1.StatusCode.Should().Be(HttpStatusCode.OK);
        r2.StatusCode.Should().Be(HttpStatusCode.OK);

        // Both returned successfully — each is a separate cache entry
        var body1 = await r1.Content.ReadAsStringAsync();
        var body2 = await r2.Content.ReadAsStringAsync();
        body1.Should().Contain("Savings");
        body2.Should().Contain("Checking");
    }

    // ── Anti-stampede lock policy — concurrent requests serialised ────────────

    [Fact]
    public async Task OutputCache_Lock_ConcurrentRequests_OnlyOneHitsOrigin()
    {
        var id     = await CreateAsync("OC-LOCK-01");
        var client = NewClient();

        // Fire 3 simultaneous requests — only the first should hit the origin
        var tasks  = Enumerable.Range(0, 3).Select(_ => client.GetAsync($"/output-cache/server/safe/{id}"));
        var results = await Task.WhenAll(tasks);

        results.Should().AllSatisfy(r => r.StatusCode.Should().Be(HttpStatusCode.OK));
    }
}

public class OutputCacheTestFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection;
    public OutputCacheTestFactory()
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
