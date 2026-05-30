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
/// Custom factory subclass keeps the SQLite connection alive for the factory's lifetime.
/// This avoids the Quartz static LogProvider capturing a disposed LoggerFactory
/// that occurs when WithWebHostBuilder creates a second host per-test.
/// </summary>
public class QuartzTestFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection;

    public QuartzTestFactory()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<BankingDbContext>));
            if (descriptor is not null) services.Remove(descriptor);

            services.AddDbContext<BankingDbContext>(options =>
                options.UseSqlite(_connection));
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _connection.Dispose();
    }
}

/// <summary>
/// Lesson 09-B integration tests — Quartz.NET IJob + ITrigger
///
/// Strategy: manually trigger the job via the /quartz/trigger endpoint
/// and poll /quartz/history until a Completed run appears.
/// This avoids depending on the cron schedule (every minute).
/// </summary>
public class QuartzJobTests : IClassFixture<QuartzTestFactory>, IDisposable
{
    private readonly QuartzTestFactory _factory;
    private readonly HttpClient _client;

    public QuartzJobTests(QuartzTestFactory factory)
    {
        _factory = factory;
        _client = _factory.CreateClient();

        // Ensure DB is up to date — safe to call multiple times
        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<BankingDbContext>().Database.Migrate();
    }

    public void Dispose() => _client.Dispose();

    private async Task<HistoryEntry[]> WaitForCompletedAsync(int count = 1, int timeoutMs = 3000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            var result = await _client.GetFromJsonAsync<HistoryEntry[]>("/quartz/history");
            if (result?.Count(r => r.Status == "Completed") >= count)
                return result!;
            await Task.Delay(100);
        }
        return await _client.GetFromJsonAsync<HistoryEntry[]>("/quartz/history") ?? [];
    }

    // ── Endpoints ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetHistory_Returns200()
    {
        var response = await _client.GetAsync("/quartz/history");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Trigger_Returns202()
    {
        var response = await _client.PostAsync("/quartz/trigger", null);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task Reset_Returns204()
    {
        var response = await _client.DeleteAsync("/quartz/history/reset");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    // ── Job execution via manual trigger ─────────────────────────────────────

    [Fact]
    public async Task AfterTrigger_JobRunsAndRecordsCompletedStatus()
    {
        await _client.DeleteAsync("/quartz/history/reset");
        await _client.PostAsync("/quartz/trigger", null);

        var history = await WaitForCompletedAsync(1);
        Assert.Contains(history, r => r.Status == "Completed");
    }

    [Fact]
    public async Task CompletedRun_HasFinishedAtAndAccountsProcessed()
    {
        await _client.DeleteAsync("/quartz/history/reset");
        await _client.PostAsync("/quartz/trigger", null);

        var history = await WaitForCompletedAsync(1);
        var completed = history.FirstOrDefault(r => r.Status == "Completed");
        Assert.NotNull(completed);
        Assert.NotNull(completed!.FinishedAt);
        Assert.True(completed.AccountsProcessed > 0);
    }

    [Fact]
    public async Task TriggerTwice_BothRunsRecorded()
    {
        await _client.DeleteAsync("/quartz/history/reset");
        await _client.PostAsync("/quartz/trigger", null);
        // Wait for first to complete before second (DisallowConcurrentExecution)
        await WaitForCompletedAsync(1);
        await _client.PostAsync("/quartz/trigger", null);

        var history = await WaitForCompletedAsync(2, timeoutMs: 5000);
        Assert.True(history.Count(r => r.Status == "Completed") >= 2);
    }

    [Fact]
    public async Task AfterReset_HistoryIsEmpty()
    {
        await _client.PostAsync("/quartz/trigger", null);
        await WaitForCompletedAsync(1);
        await _client.DeleteAsync("/quartz/history/reset");

        var history = await _client.GetFromJsonAsync<HistoryEntry[]>("/quartz/history");
        Assert.Empty(history ?? []);
    }

    // ── JobHistoryStore via DI ────────────────────────────────────────────────

    [Fact]
    public async Task Store_SameInstanceSharedBetweenJobAndController()
    {
        var store = _factory.Services.GetRequiredService<JobHistoryStore>();
        await _client.DeleteAsync("/quartz/history/reset");

        store.Add(new JobExecution(Guid.NewGuid(), DateTime.UtcNow, null, 0, "Running"));

        var history = await _client.GetFromJsonAsync<HistoryEntry[]>("/quartz/history");
        Assert.Single(history ?? []);
    }

    private record HistoryEntry(Guid RunId, DateTime StartedAt, DateTime? FinishedAt, int AccountsProcessed, string Status);
}
