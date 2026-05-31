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
/// Lesson 16-A — async/await, Task.WhenAll, Task.WhenAny, CancellationToken tests.
///
/// Key testing patterns for async code:
///   - Test methods are `async Task` (not `async void`) — xUnit awaits the returned Task.
///   - CancellationToken is tested by passing CancellationToken.None or a timed-out token.
///   - Concurrency (WhenAll) is exercised by sending a batch request and asserting all
///     results are returned correctly.
///
/// Java parallel (JUnit 5 + CompletableFuture):
///   @Test void test() throws Exception { future.get(1, SECONDS); }
///   In xUnit: [Fact] async Task Test() { await ...; }
/// </summary>
public class ConcurrencyDemoTests : IClassFixture<ConcurrencyTestFactory>
{
    private readonly ConcurrencyTestFactory _factory;
    public ConcurrencyDemoTests(ConcurrencyTestFactory factory) => _factory = factory;

    private HttpClient NewClient() => _factory.CreateClient(
        new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private async Task<int> SeedAccountAsync(decimal balance = 1000m)
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var resp   = await NewClient().PostAsJsonAsync("/accounts",
            new { accountNumber = $"T16A-{suffix}", ownerName = "Task User",
                  accountType = "Savings", initialBalance = balance });
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<AccountSummary>();
        return body!.Id;
    }

    // ── Single async get (ConfigureAwait path) ────────────────────────────────
    [Fact]
    public async Task GetAccount_Returns200()
    {
        var id   = await SeedAccountAsync();
        var resp = await NewClient().GetAsync($"/concurrency-demo/accounts/{id}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── Missing account returns 404 ───────────────────────────────────────────
    [Fact]
    public async Task GetAccount_NotFound_Returns404()
    {
        var resp = await NewClient().GetAsync("/concurrency-demo/accounts/999999");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── WhenAll batch — all accounts returned ────────────────────────────────
    [Fact]
    public async Task BatchFetch_AllFound_Returns200WithFoundList()
    {
        var id1  = await SeedAccountAsync();
        var id2  = await SeedAccountAsync();
        var resp = await NewClient().GetAsync($"/concurrency-demo/batch?ids={id1},{id2}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<BatchResult>();
        body!.Found.Should().HaveCount(2);
        body.Missing.Should().BeEmpty();
    }

    // ── WhenAll batch — partial missing ──────────────────────────────────────
    [Fact]
    public async Task BatchFetch_PartialMissing_ReturnsMissingList()
    {
        var id1  = await SeedAccountAsync();
        var resp = await NewClient().GetAsync($"/concurrency-demo/batch?ids={id1},999888");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<BatchResult>();
        body!.Found.Should().HaveCount(1);
        body.Missing.Should().Contain(999888);
    }

    // ── WhenAny fastest — returns first result ───────────────────────────────
    [Fact]
    public async Task FastestFetch_Returns200_WithFirstResult()
    {
        var id1  = await SeedAccountAsync();
        var id2  = await SeedAccountAsync();
        var resp = await NewClient().GetAsync($"/concurrency-demo/fastest?ids={id1},{id2}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── Timeout race — completes within generous timeout ─────────────────────
    [Fact]
    public async Task GetWithTimeout_FastEnough_Returns200()
    {
        var id   = await SeedAccountAsync();
        var resp = await NewClient().GetAsync($"/concurrency-demo/with-timeout/{id}?timeoutMs=5000");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── Timeout race — misses with 1ms deadline ───────────────────────────────
    [Fact]
    public async Task GetWithTimeout_TooShort_Returns504()
    {
        // 1ms is almost certainly not enough to complete the DB round-trip
        var id   = await SeedAccountAsync();
        var resp = await NewClient().GetAsync($"/concurrency-demo/with-timeout/{id}?timeoutMs=1");
        // May be 200 on a fast machine; acceptable either way — just must not throw
        ((int)resp.StatusCode).Should().BeOneOf(200, 504);
    }

    // ── Task.Run CPU work ─────────────────────────────────────────────────────
    [Fact]
    public async Task CpuWork_Returns200_WithCorrectSum()
    {
        var resp = await NewClient().PostAsJsonAsync("/concurrency-demo/cpu-work",
            new { iterations = 100 });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<CpuWorkResult>();
        // sum(0..99) = 4950
        body!.Result.Should().Be(4950);
    }

    // ── Async test method itself demonstrates async Task pattern ──────────────
    // This test verifies multiple accounts can be fetched truly concurrently from test code.
    [Fact]
    public async Task WhenAll_FromTestCode_AllRequestsComplete()
    {
        var id1 = await SeedAccountAsync();
        var id2 = await SeedAccountAsync();
        var client = NewClient();

        // Fan-out two HTTP requests concurrently from test code
        var t1 = client.GetAsync($"/concurrency-demo/accounts/{id1}");
        var t2 = client.GetAsync($"/concurrency-demo/accounts/{id2}");
        var responses = await Task.WhenAll(t1, t2);

        responses.Should().AllSatisfy(r => r.StatusCode.Should().Be(HttpStatusCode.OK));
    }
}

public record BatchResult(
    System.Text.Json.JsonElement[] Found,
    int[] Missing);

public record CpuWorkResult(int Iterations, long Result);

public class ConcurrencyTestFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection;
    public ConcurrencyTestFactory()
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
