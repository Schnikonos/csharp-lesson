using System.Net;
using System.Net.Http.Json;
using Lesson.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lesson.Tests;

/// <summary>
/// Lesson 08-C integration tests — Channel&lt;T&gt; + BackgroundService
///
/// Key testing challenge: the background service runs asynchronously,
/// so tests must poll / await the processed log with a small timeout.
/// </summary>
public class ChannelHostedServiceTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly SqliteConnection _connection;
    private readonly HttpClient _client;

    public ChannelHostedServiceTests(WebApplicationFactory<Program> factory)
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

    /// <summary>Polls the /outbox/processed endpoint until count &gt;= expected or timeout.</summary>
    private async Task<ProcessedEntry[]> WaitForProcessedAsync(int expectedCount, int timeoutMs = 2000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            var result = await _client.GetFromJsonAsync<ProcessedEntry[]>("/outbox/processed");
            if ((result?.Length ?? 0) >= expectedCount)
                return result!;
            await Task.Delay(50);
        }
        return await _client.GetFromJsonAsync<ProcessedEntry[]>("/outbox/processed") ?? [];
    }

    // ── Producer: POST /outbox ────────────────────────────────────────────────

    [Fact]
    public async Task Publish_Returns202Accepted()
    {
        var response = await _client.PostAsJsonAsync("/outbox",
            new { eventType = "AccountCreated", payload = new { id = 1 } });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task Publish_ResponseContainsMessageId()
    {
        var response = await _client.PostAsJsonAsync("/outbox",
            new { eventType = "TestEvent", payload = new { x = 42 } });
        var body = await response.Content.ReadFromJsonAsync<PublishBody>();
        Assert.NotEqual(Guid.Empty, body?.Id);
        Assert.True(body?.Queued);
    }

    // ── Consumer: background service processes the message ───────────────────

    [Fact]
    public async Task AfterPublish_MessageIsProcessedByBackgroundService()
    {
        await _client.PostAsJsonAsync("/outbox",
            new { eventType = "OrderPlaced", payload = new { orderId = 99 } });

        var processed = await WaitForProcessedAsync(1);
        Assert.Contains(processed, p => p.EventType == "OrderPlaced");
    }

    [Fact]
    public async Task AfterMultiplePublishes_AllMessagesProcessed()
    {
        await _client.PostAsJsonAsync("/outbox",
            new { eventType = "Evt1", payload = new { } });
        await _client.PostAsJsonAsync("/outbox",
            new { eventType = "Evt2", payload = new { } });
        await _client.PostAsJsonAsync("/outbox",
            new { eventType = "Evt3", payload = new { } });

        var processed = await WaitForProcessedAsync(3);
        Assert.True(processed.Length >= 3);
    }

    [Fact]
    public async Task ProcessedEntry_ContainsCorrectEventType()
    {
        await _client.PostAsJsonAsync("/outbox",
            new { eventType = "SpecialEvent", payload = new { value = "hello" } });

        var processed = await WaitForProcessedAsync(1);
        var entry = processed.FirstOrDefault(p => p.EventType == "SpecialEvent");
        Assert.NotNull(entry);
    }

    [Fact]
    public async Task ProcessedEntry_PayloadIsSerializedJson()
    {
        await _client.PostAsJsonAsync("/outbox",
            new { eventType = "PayloadCheck", payload = new { key = "v1" } });

        var processed = await WaitForProcessedAsync(1);
        var entry = processed.FirstOrDefault(p => p.EventType == "PayloadCheck");
        Assert.NotNull(entry?.Payload);
        Assert.Contains("v1", entry!.Payload);
    }

    private record PublishBody(Guid Id, bool Queued);
    private record ProcessedEntry(Guid Id, string EventType, string Payload, DateTime CreatedAt);
}
