using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Lesson.Features.Accounts.Queries;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Lesson.Data;

namespace Lesson.Tests;

/// <summary>
/// Lesson 18-B — Pipeline behaviours: LoggingBehavior, ValidationBehavior, TransactionBehavior.
///
/// The three behaviours execute in order around every handler.
/// We verify end-to-end observable outcomes:
///   - Commands still succeed with all 3 behaviours wired (LoggingBehavior, TransactionBehavior run)
///   - ValidationBehavior intercepts invalid requests before the handler runs
///   - Queries pass through all behaviours unmodified
/// </summary>
public class CqrsPipelineTests : IClassFixture<CqrsPipelineFactory>
{
    private readonly CqrsPipelineFactory _factory;
    public CqrsPipelineTests(CqrsPipelineFactory factory) => _factory = factory;

    private HttpClient NewClient() => _factory.CreateClient(
        new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    // Command succeeds with all 3 behaviours in the pipeline
    [Fact]
    public async Task CreateAccount_WithAllBehaviours_Returns201()
    {
        var resp = await NewClient().PostAsJsonAsync("/cqrs/accounts",
            new { accountNumber = "PIPE-001", ownerName = "Alice",
                  accountType = "Savings", initialBalance = 1000m });

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    // ValidationBehavior intercepts before the handler — no account created
    [Fact]
    public async Task CreateAccount_InvalidRequest_Returns400()
    {
        var resp = await NewClient().PostAsJsonAsync("/cqrs/accounts",
            new { accountNumber = "", ownerName = "", accountType = "Savings", initialBalance = 0m });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // Query path — all behaviours fire and result is returned normally
    [Fact]
    public async Task GetAllAccounts_WithBehaviours_Returns200()
    {
        var resp = await NewClient().GetAsync("/cqrs/accounts");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // Multiple commands in sequence — TransactionBehavior wraps each individually
    [Fact]
    public async Task MultipleCommands_AllSucceed_QueryReflectsAll()
    {
        var client = NewClient();
        for (int i = 10; i <= 12; i++)
            (await client.PostAsJsonAsync("/cqrs/accounts",
                new { accountNumber = $"PIPE-{i:D3}", ownerName = $"User{i}",
                      accountType = "Savings", initialBalance = (decimal)i * 100 }))
                .StatusCode.Should().Be(HttpStatusCode.Created);

        var all = await (await client.GetAsync("/cqrs/accounts"))
            .Content.ReadFromJsonAsync<List<AccountSummaryDto>>();

        all!.Count(a => a.AccountNumber.StartsWith("PIPE-")).Should().BeGreaterThanOrEqualTo(3);
    }
}

// ── Factory ──────────────────────────────────────────────────────────────────
public class CqrsPipelineFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection;

    public CqrsPipelineFactory()
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
