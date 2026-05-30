using System.Net;
using System.Net.Http.Json;
using Lesson.Data;
using Lesson.ScheduledTasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lesson.Tests;

/// <summary>
/// Lesson 09-A integration tests — PeriodicTimer BackgroundService
///
/// Testing strategy:
///   • The background service timer is non-deterministic in a test environment;
///     we avoid relying on it firing within a test and instead test the
///     controller endpoints + JobHistoryStore directly via DI resolution.
///   • This mirrors best practice: test the *observable API* (HTTP endpoints)
///     and the *unit logic* (store manipulation) separately from the timer loop.
///   • For the service execution path, a unit test drives ExecuteAsync directly.
/// </summary>
public class ScheduledTaskBasicTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly SqliteConnection _connection;
    private readonly HttpClient _client;

    public ScheduledTaskBasicTests(WebApplicationFactory<Program> factory)
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _factory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(DbContextOptions<BankingDbContext>));
                if (descriptor is not null) services.Remove(descriptor);

                services.AddDbContext<BankingDbContext>(options =>
                    options.UseSqlite(_connection));

                var sp = services.BuildServiceProvider();
                using var scope = sp.CreateScope();
                scope.ServiceProvider
                     .GetRequiredService<BankingDbContext>()
                     .Database.Migrate();
            }));

        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
        _connection.Dispose();
    }

    // ── History endpoint ────────────────────────────────────────────────────

    [Fact]
    public async Task GetHistory_Returns200()
    {
        var response = await _client.GetAsync("/scheduled-tasks/history");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetHistory_ReturnsEmptyArrayByDefault()
    {
        // Reset first so previous test runs don't bleed in
        await _client.DeleteAsync("/scheduled-tasks/history/reset");
        var history = await _client.GetFromJsonAsync<HistoryEntry[]>("/scheduled-tasks/history");
        Assert.Empty(history ?? []);
    }

    [Fact]
    public async Task Reset_Returns204()
    {
        var response = await _client.DeleteAsync("/scheduled-tasks/history/reset");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    // ── JobHistoryStore via DI ───────────────────────────────────────────────

    [Fact]
    public async Task AddToStore_AppearsInHistoryEndpoint()
    {
        await _client.DeleteAsync("/scheduled-tasks/history/reset");

        // Pre-populate the store via DI (same singleton the controller uses)
        var store = _factory.Services.GetRequiredService<JobHistoryStore>();
        var run = new JobExecution(Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow.AddSeconds(1), 5, "Completed");
        store.Add(run);

        var history = await _client.GetFromJsonAsync<HistoryEntry[]>("/scheduled-tasks/history");
        Assert.Contains(history ?? [], h => h.RunId == run.RunId);
    }

    [Fact]
    public async Task AfterReset_HistoryIsEmpty()
    {
        var store = _factory.Services.GetRequiredService<JobHistoryStore>();
        store.Add(new JobExecution(Guid.NewGuid(), DateTime.UtcNow, null, 0, "Running"));

        await _client.DeleteAsync("/scheduled-tasks/history/reset");

        var history = await _client.GetFromJsonAsync<HistoryEntry[]>("/scheduled-tasks/history");
        Assert.Empty(history ?? []);
    }

    [Fact]
    public async Task MultipleRuns_AllAppearInHistory()
    {
        await _client.DeleteAsync("/scheduled-tasks/history/reset");

        var store = _factory.Services.GetRequiredService<JobHistoryStore>();
        for (var i = 0; i < 3; i++)
            store.Add(new JobExecution(Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow, i + 1, "Completed"));

        var history = await _client.GetFromJsonAsync<HistoryEntry[]>("/scheduled-tasks/history");
        Assert.Equal(3, history?.Length ?? 0);
    }

    // ── Unit test: service logic without the timer ──────────────────────────

    [Fact]
    public async Task InterestCalculationService_ExecutesOneTickAndRecordsCompletedRun()
    {
        var store = new JobHistoryStore();
        var logger = new Microsoft.Extensions.Logging.Abstractions.NullLogger<Lesson.HostedServices.InterestCalculationService>();

        // Use a very short period so the first tick fires quickly
        var svc = new Lesson.HostedServices.InterestCalculationService(store, logger, TimeSpan.FromMilliseconds(50));

        using var cts = new CancellationTokenSource();

        await svc.StartAsync(cts.Token);

        // Wait long enough for at least one tick to complete
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (DateTime.UtcNow < deadline && !store.History.Any(r => r.Status == "Completed"))
            await Task.Delay(20);

        // Cancel only AFTER we have a completed run (avoids race with the 50ms job delay)
        await cts.CancelAsync();
        await svc.StopAsync(CancellationToken.None);

        Assert.True(store.History.Count >= 1);
        Assert.Contains(store.History, r => r.Status == "Completed");
    }

    private record HistoryEntry(Guid RunId, DateTime StartedAt, DateTime? FinishedAt, int AccountsProcessed, string Status);
}
