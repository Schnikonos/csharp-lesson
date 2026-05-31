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
/// Lesson 16-B — Thread-safety tests.
///
/// Key patterns demonstrated:
///   1. Concurrent HTTP requests via Task.WhenAll — exercises SemaphoreSlim throttle.
///   2. Verifying Interlocked counters are monotonically increasing under concurrency.
///   3. Parallel.ForEachAsync endpoint — verifies all accounts processed without race errors.
///   4. ConcurrentDictionary cache — second call returns the cached value.
///
/// Java parallel (JUnit 5 + ExecutorService):
///   ExecutorService pool = Executors.newFixedThreadPool(5);
///   List&lt;Future&gt; futures = IntStream.range(0,5).mapToObj(i -> pool.submit(task)).toList();
///   futures.forEach(f -> f.get()); // assert no exceptions
///   In xUnit: Task.WhenAll(requests) — much simpler.
/// </summary>
public class ThreadSafetyTests : IClassFixture<ThreadSafetyTestFactory>
{
    private readonly ThreadSafetyTestFactory _factory;
    public ThreadSafetyTests(ThreadSafetyTestFactory factory) => _factory = factory;

    private HttpClient NewClient() => _factory.CreateClient(
        new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private async Task<int> SeedAccountAsync(decimal balance = 2000m)
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var resp = await NewClient().PostAsJsonAsync("/accounts",
            new { accountNumber = $"T16B-{suffix}", ownerName = "Thread User",
                  accountType = "Savings", initialBalance = balance });
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<AccountSummary>();
        return body!.Id;
    }

    // ── Stats endpoint — lock-guarded counter increments ─────────────────────
    [Fact]
    public async Task Stats_Returns200()
    {
        var resp = await NewClient().GetAsync("/thread-safety/stats");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── Concurrent stats — Interlocked counter never goes backward ────────────
    [Fact]
    public async Task Stats_ConcurrentRequests_CounterMonotonicallyIncreases()
    {
        var client = NewClient();
        // Fire 5 concurrent requests
        var tasks = Enumerable.Range(0, 5)
            .Select(_ => client.GetAsync("/thread-safety/stats"))
            .ToList();
        var responses = await Task.WhenAll(tasks);

        responses.Should().AllSatisfy(r => r.StatusCode.Should().Be(HttpStatusCode.OK));

        var counters = new List<long>();
        foreach (var r in responses)
        {
            var body = await r.Content.ReadFromJsonAsync<StatsResult>();
            counters.Add(body!.AtomicCounter);
        }
        // All counters should be distinct positive values (no two increments aliased)
        counters.Should().OnlyHaveUniqueItems();
        counters.Should().AllSatisfy(c => c.Should().BePositive());
    }

    // ── SemaphoreSlim — sequential requests each succeed ─────────────────────
    // The semaphore throttle is demonstrated conceptually; SQLite in-memory is
    // single-connection so true concurrent DB reads are serialised at the transport layer.
    // In a real multi-connection DB (Postgres/SQL Server) the semaphore would visibly
    // cap the number of concurrent in-flight DB calls.
    [Fact]
    public async Task Semaphore_SequentialRequests_AllSucceed()
    {
        var id = await SeedAccountAsync();
        var client = NewClient();

        for (var i = 0; i < 4; i++)
        {
            var resp = await client.GetAsync($"/thread-safety/semaphore/{id}");
            resp.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    // ── SemaphoreSlim — not found returns 404 ────────────────────────────────
    [Fact]
    public async Task Semaphore_NotFound_Returns404()
    {
        var resp = await NewClient().GetAsync("/thread-safety/semaphore/999999");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── ConcurrentDictionary — second call returns cached value ───────────────
    [Fact]
    public async Task Cache_SecondCall_ReturnsCachedSource()
    {
        // Prime the cache via semaphore endpoint (which calls TryAdd)
        var id = await SeedAccountAsync();
        await NewClient().GetAsync($"/thread-safety/semaphore/{id}");

        // Now hit the cache endpoint — should return "ConcurrentDictionary"
        var resp = await NewClient().GetAsync($"/thread-safety/cache/{id}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<CacheResult>();
        body!.Source.Should().Be("ConcurrentDictionary");
    }

    // ── Parallel.ForEachAsync — all accounts processed ────────────────────────
    [Fact]
    public async Task ParallelInterest_Returns200_WithAllAccountsProcessed()
    {
        var id1 = await SeedAccountAsync(1000m);
        var id2 = await SeedAccountAsync(2000m);
        var id3 = await SeedAccountAsync(3000m);

        var resp = await NewClient().PostAsJsonAsync("/thread-safety/parallel-interest",
            new { accountIds = new[] { id1, id2, id3 }, annualRatePercent = 5.0 });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<ParallelResult>();
        body!.Processed.Should().HaveCount(3);
        body.Processed.Should().AllSatisfy(s => s.Should().Contain("daily-interest="));
    }

    // ── Parallel.ForEachAsync — missing account reported without crash ─────────
    [Fact]
    public async Task ParallelInterest_WithMissingId_ReportsNotFound()
    {
        var id1 = await SeedAccountAsync();
        var resp = await NewClient().PostAsJsonAsync("/thread-safety/parallel-interest",
            new { accountIds = new[] { id1, 999777 }, annualRatePercent = 3.0 });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<ParallelResult>();
        body!.Processed.Should().Contain(s => s.Contains("NOT FOUND"));
    }
}

public record StatsResult(int LockGuardedRequestCount, long AtomicCounter, int CacheEntries, int ThreadId);
public record CacheResult(int Id, decimal? CachedBalance, decimal? Balance, string Source);
public record ParallelResult(List<string> Processed);

public class ThreadSafetyTestFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection;
    public ThreadSafetyTestFactory()
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
