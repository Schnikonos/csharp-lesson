using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lesson.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lesson.Tests;

/// <summary>
/// Lesson 08-A integration tests — C# Events and Delegates
///
/// Verifies:
///   • Publishing via DomainEventBus raises the event (PaymentAuditSubscriber receives it)
///   • Multiple publishes accumulate in the audit log
///   • Audit log returns correct data
///   • Delegate demo endpoint returns expected fields
/// </summary>
public class EventBasicTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly SqliteConnection _connection;
    private readonly HttpClient _client;

    public EventBasicTests(WebApplicationFactory<Program> factory)
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

    // ── Event publishing ──────────────────────────────────────────────────────

    [Fact]
    public async Task PublishPayment_Returns200()
    {
        var response = await _client.PostAsJsonAsync("/event-demo/payment",
            new { fromAccount = "ACC001", toAccount = "ACC002", amount = 100m });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PublishPayment_ResponseContainsPaymentId()
    {
        var response = await _client.PostAsJsonAsync("/event-demo/payment",
            new { fromAccount = "ACC001", toAccount = "ACC002", amount = 100m });
        var body = await response.Content.ReadFromJsonAsync<PublishBody>();
        Assert.True(body?.Published);
        Assert.NotEqual(Guid.Empty, body?.PaymentId);
    }

    // ── Audit subscriber ──────────────────────────────────────────────────────

    [Fact]
    public async Task AfterPublish_AuditLogContainsEntry()
    {
        await _client.PostAsJsonAsync("/event-demo/payment",
            new { fromAccount = "AUDT01", toAccount = "AUDT02", amount = 50m });

        var audit = await _client.GetFromJsonAsync<AuditEntry[]>("/event-demo/audit");
        Assert.Contains(audit ?? [], e => e.FromAccount == "AUDT01");
    }

    [Fact]
    public async Task MultiplePublishes_AllRecordedInAuditLog()
    {
        var countBefore = (await _client.GetFromJsonAsync<AuditEntry[]>("/event-demo/audit"))?.Length ?? 0;

        await _client.PostAsJsonAsync("/event-demo/payment",
            new { fromAccount = "X001", toAccount = "X002", amount = 10m });
        await _client.PostAsJsonAsync("/event-demo/payment",
            new { fromAccount = "X003", toAccount = "X004", amount = 20m });

        var audit = await _client.GetFromJsonAsync<AuditEntry[]>("/event-demo/audit");
        Assert.True((audit?.Length ?? 0) >= countBefore + 2);
    }

    [Fact]
    public async Task AuditEntry_ContainsCorrectAmount()
    {
        await _client.PostAsJsonAsync("/event-demo/payment",
            new { fromAccount = "AMTCHK", toAccount = "ACC002", amount = 777m });

        var audit = await _client.GetFromJsonAsync<AuditEntry[]>("/event-demo/audit");
        var entry = audit?.FirstOrDefault(e => e.FromAccount == "AMTCHK");
        Assert.NotNull(entry);
        Assert.Equal(777m, entry!.Amount);
    }

    // ── Delegate demo ─────────────────────────────────────────────────────────

    [Fact]
    public async Task DelegateDemo_Returns200()
    {
        var response = await _client.GetAsync("/event-demo/delegate-demo");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DelegateDemo_FuncResultIs120()
    {
        var body = await _client.GetFromJsonAsync<DelegateDemoBody>("/event-demo/delegate-demo");
        Assert.Equal(120m, body?.FuncResult);
    }

    private record PublishBody(bool Published, Guid PaymentId);
    private record AuditEntry(Guid PaymentId, string FromAccount, string ToAccount, decimal Amount, DateTime OccurredAt);
    private record DelegateDemoBody(bool ActionUsed, decimal FuncResult, bool MulticastFired);
}
