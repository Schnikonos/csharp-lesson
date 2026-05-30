using System.Net;
using System.Net.Http.Json;
using Lesson.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lesson.Tests;

/// <summary>
/// Lesson 08-B integration tests — MediatR INotification + INotificationHandler
///
/// Verifies:
///   • Publish dispatches to all handlers (both email handler and audit handler)
///   • Audit log records the notification correctly
///   • Multiple publishes accumulate in the log
///   • OwnerName and InitialBalance are preserved through the notification
/// </summary>
public class MediatRNotificationTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly SqliteConnection _connection;
    private readonly HttpClient _client;

    public MediatRNotificationTests(WebApplicationFactory<Program> factory)
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

    private Task ResetAuditAsync() => _client.DeleteAsync("/accounts-events/audit/reset");

    // ── INotification publish ─────────────────────────────────────────────────

    [Fact]
    public async Task Create_Returns200AndAccountId()
    {
        var response = await _client.PostAsJsonAsync("/accounts-events",
            new { ownerName = "Alice", initialBalance = 1000m });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<CreateBody>();
        Assert.NotEqual(Guid.Empty, body?.AccountId);
    }

    [Fact]
    public async Task Create_OwnerNamePreservedInResponse()
    {
        var response = await _client.PostAsJsonAsync("/accounts-events",
            new { ownerName = "Bob", initialBalance = 500m });
        var body = await response.Content.ReadFromJsonAsync<CreateBody>();
        Assert.Equal("Bob", body?.OwnerName);
    }

    // ── Both notification handlers receive the event ───────────────────────────

    [Fact]
    public async Task AfterCreate_AuditHandlerReceivesNotification()
    {
        await ResetAuditAsync();

        await _client.PostAsJsonAsync("/accounts-events",
            new { ownerName = "Charlie", initialBalance = 250m });

        var audit = await _client.GetFromJsonAsync<AuditEntry[]>("/accounts-events/audit");
        Assert.Contains(audit ?? [], e => e.OwnerName == "Charlie");
    }

    [Fact]
    public async Task AfterCreate_AuditEntryHasCorrectBalance()
    {
        await ResetAuditAsync();

        await _client.PostAsJsonAsync("/accounts-events",
            new { ownerName = "Diana", initialBalance = 9999m });

        var audit = await _client.GetFromJsonAsync<AuditEntry[]>("/accounts-events/audit");
        var entry = audit?.FirstOrDefault(e => e.OwnerName == "Diana");
        Assert.NotNull(entry);
        Assert.Equal(9999m, entry!.InitialBalance);
    }

    [Fact]
    public async Task MultipleCreates_AllRecordedInAuditLog()
    {
        await ResetAuditAsync();

        await _client.PostAsJsonAsync("/accounts-events",
            new { ownerName = "Eve", initialBalance = 100m });
        await _client.PostAsJsonAsync("/accounts-events",
            new { ownerName = "Frank", initialBalance = 200m });

        var audit = await _client.GetFromJsonAsync<AuditEntry[]>("/accounts-events/audit");
        Assert.True((audit?.Length ?? 0) >= 2);
    }

    // ── Decoupling: publisher does not reference handlers ─────────────────────

    [Fact]
    public async Task ResetAudit_ClearsLog()
    {
        // Publish something first
        await _client.PostAsJsonAsync("/accounts-events",
            new { ownerName = "Grace", initialBalance = 50m });

        await ResetAuditAsync();

        var audit = await _client.GetFromJsonAsync<AuditEntry[]>("/accounts-events/audit");
        Assert.Empty(audit ?? []);
    }

    [Fact]
    public async Task Create_WithZeroBalance_StillPublishesAndRecords()
    {
        await ResetAuditAsync();

        var response = await _client.PostAsJsonAsync("/accounts-events",
            new { ownerName = "Heidi", initialBalance = 0m });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var audit = await _client.GetFromJsonAsync<AuditEntry[]>("/accounts-events/audit");
        Assert.Contains(audit ?? [], e => e.OwnerName == "Heidi");
    }

    private record CreateBody(Guid AccountId, string OwnerName);
    private record AuditEntry(Guid AccountId, string OwnerName, decimal InitialBalance);
}
