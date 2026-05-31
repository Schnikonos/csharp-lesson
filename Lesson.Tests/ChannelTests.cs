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
/// Lesson 16-C — Channel&lt;T&gt;, IAsyncEnumerable, ValueTask, ReaderWriterLockSlim tests.
///
/// Key testing patterns:
///   - Channel enqueue/drain round-trip verifies producer/consumer pipeline.
///   - IAsyncEnumerable streaming: ASP.NET Core serialises the async enumerable
///     to a JSON array; the test reads the whole response and parses it.
///   - ValueTask cache hit: second call returns the same value without a DB round-trip.
///   - ReaderWriterLockSlim: concurrent reads succeed while a write is pending;
///     tested by firing flag + read concurrently and asserting no errors.
/// </summary>
public class ChannelTests : IClassFixture<ChannelTestFactory>
{
    private readonly ChannelTestFactory _factory;
    public ChannelTests(ChannelTestFactory factory) => _factory = factory;

    private HttpClient NewClient() => _factory.CreateClient(
        new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private async Task<int> SeedAccountAsync(decimal balance = 5000m)
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var resp = await NewClient().PostAsJsonAsync("/accounts",
            new { accountNumber = $"T16C-{suffix}", ownerName = "Channel User",
                  accountType = "Savings", initialBalance = balance });
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<AccountSummary>();
        return body!.Id;
    }

    // ── Channel enqueue + drain round-trip ────────────────────────────────────
    [Fact]
    public async Task Enqueue_ThenDrain_ReturnsQueuedIds()
    {
        var id1 = await SeedAccountAsync();
        var id2 = await SeedAccountAsync();

        var enqResp = await NewClient().PostAsJsonAsync("/advanced-concurrency/enqueue",
            new { accountIds = new[] { id1, id2 } });
        enqResp.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var drainResp = await NewClient().GetAsync("/advanced-concurrency/drain");
        drainResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await drainResp.Content.ReadFromJsonAsync<DrainResult>();
        body!.Drained.Should().Contain(id1).And.Contain(id2);
    }

    // ── Drain on empty channel returns empty list ─────────────────────────────
    [Fact]
    public async Task Drain_WhenEmpty_ReturnsEmptyList()
    {
        // Drain any residual items first
        await NewClient().GetAsync("/advanced-concurrency/drain");

        var resp = await NewClient().GetAsync("/advanced-concurrency/drain");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<DrainResult>();
        body!.Drained.Should().BeEmpty();
    }

    // ── IAsyncEnumerable streaming — all accounts returned ────────────────────
    [Fact]
    public async Task Stream_KnownIds_ReturnsAllResults()
    {
        var id1 = await SeedAccountAsync(1000m);
        var id2 = await SeedAccountAsync(2000m);

        var resp = await NewClient().GetAsync($"/advanced-concurrency/stream?ids={id1},{id2}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var items = await resp.Content.ReadFromJsonAsync<StreamItem[]>();
        items.Should().HaveCount(2);
        items.Should().AllSatisfy(i => i.Status.Should().Be("ok"));
    }

    // ── IAsyncEnumerable streaming — missing IDs reported inline ──────────────
    [Fact]
    public async Task Stream_WithMissingId_ReportsNotFound()
    {
        var id1 = await SeedAccountAsync();

        var resp = await NewClient().GetAsync($"/advanced-concurrency/stream?ids={id1},999666");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var items = await resp.Content.ReadFromJsonAsync<StreamItem[]>();
        items.Should().HaveCount(2);
        items.Should().Contain(i => i.Status == "ok");
        items.Should().Contain(i => i.Status == "not found");
    }

    // ── ValueTask — first call hits DB, second call hits cache ────────────────
    [Fact]
    public async Task ValueTask_SecondCall_ReturnsSameBalance()
    {
        var id = await SeedAccountAsync(7777m);

        var r1 = await NewClient().GetAsync($"/advanced-concurrency/valuetask/{id}");
        r1.StatusCode.Should().Be(HttpStatusCode.OK);
        var b1 = await r1.Content.ReadFromJsonAsync<ValueTaskResult>();

        var r2 = await NewClient().GetAsync($"/advanced-concurrency/valuetask/{id}");
        r2.StatusCode.Should().Be(HttpStatusCode.OK);
        var b2 = await r2.Content.ReadFromJsonAsync<ValueTaskResult>();

        b1!.Balance.Should().Be(b2!.Balance);
    }

    // ── ValueTask — unknown ID returns 404 ────────────────────────────────────
    [Fact]
    public async Task ValueTask_UnknownId_Returns404()
    {
        var resp = await NewClient().GetAsync("/advanced-concurrency/valuetask/999555");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── ReaderWriterLockSlim — flag an account ────────────────────────────────
    [Fact]
    public async Task FlagAccount_Returns200()
    {
        var id   = await SeedAccountAsync();
        var resp = await NewClient().PostAsync($"/advanced-concurrency/flag/{id}", null);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── ReaderWriterLockSlim — flagged list contains previously flagged ID ────
    [Fact]
    public async Task GetFlagged_ContainsFlaggedId()
    {
        var id = await SeedAccountAsync();
        await NewClient().PostAsync($"/advanced-concurrency/flag/{id}", null);

        var resp = await NewClient().GetAsync("/advanced-concurrency/flagged");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<FlaggedResult>();
        body!.Flagged.Should().Contain(id);
    }

    // ── ReaderWriterLockSlim — concurrent reads while write is in flight ───────
    // Fires a write and multiple reads concurrently; none should error.
    [Fact]
    public async Task RWLock_ConcurrentReadsDuringWrite_AllSucceed()
    {
        var id     = await SeedAccountAsync();
        var client = NewClient();

        var write  = client.PostAsync($"/advanced-concurrency/flag/{id}", null);
        var reads  = Enumerable.Range(0, 4)
                               .Select(_ => client.GetAsync("/advanced-concurrency/flagged"))
                               .ToList();

        await Task.WhenAll(reads.Append(write));

        var allResponses = reads.Select(t => t.Result).Append(write.Result);
        allResponses.Should().AllSatisfy(r => r.StatusCode.Should().Be(HttpStatusCode.OK));
    }
}

public record DrainResult(int[] Drained);
public record StreamItem(int Id, decimal? Balance, string Status);
public record ValueTaskResult(int Id, decimal Balance);
public record FlaggedResult(int[] Flagged);

public class ChannelTestFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection;
    public ChannelTestFactory()
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
