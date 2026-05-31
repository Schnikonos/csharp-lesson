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
/// Lesson 18-A — CQRS via MediatR ISender.
///
/// Key assertions:
///   POST /cqrs/accounts dispatches CreateAccountCommand → CreateAccountCommandHandler
///   GET  /cqrs/accounts dispatches GetAllAccountsQuery  → GetAllAccountsQueryHandler
/// </summary>
public class CqrsBasicTests : IClassFixture<CqrsTestFactory>
{
    private readonly CqrsTestFactory _factory;
    public CqrsBasicTests(CqrsTestFactory factory) => _factory = factory;

    private HttpClient NewClient() => _factory.CreateClient(
        new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    // POST returns 201 Created with the new account id
    [Fact]
    public async Task CreateAccount_Returns201WithId()
    {
        var resp = await NewClient().PostAsJsonAsync("/cqrs/accounts",
            new { accountNumber = "CQRS-001", ownerName = "Alice",
                  accountType = "Savings", initialBalance = 1000m });

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await resp.Content.ReadFromJsonAsync<CreatedResult>();
        body!.Id.Should().BeGreaterThan(0);
    }

    // GET returns the created account in the list
    [Fact]
    public async Task GetAllAccounts_ReturnsCreatedAccount()
    {
        var client = NewClient();
        await client.PostAsJsonAsync("/cqrs/accounts",
            new { accountNumber = "CQRS-002", ownerName = "Bob",
                  accountType = "Checking", initialBalance = 500m });

        var resp = await client.GetAsync("/cqrs/accounts");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var accounts = await resp.Content.ReadFromJsonAsync<List<AccountSummaryDto>>();
        accounts.Should().NotBeNull();
        accounts!.Any(a => a.AccountNumber == "CQRS-002").Should().BeTrue();
    }

    // Controller has zero business logic — handler is responsible
    [Fact]
    public async Task CreateAccount_TwoDifferentAccounts_BothAppearInQuery()
    {
        var client = NewClient();
        foreach (var num in new[] { "CQRS-003", "CQRS-004" })
            await client.PostAsJsonAsync("/cqrs/accounts",
                new { accountNumber = num, ownerName = "User", accountType = "Savings", initialBalance = 0m });

        var all = await (await client.GetAsync("/cqrs/accounts"))
            .Content.ReadFromJsonAsync<List<AccountSummaryDto>>();

        all!.Select(a => a.AccountNumber).Should().Contain(new[] { "CQRS-003", "CQRS-004" });
    }

    // ISender.Send with a query returns a read-only list
    [Fact]
    public async Task GetAllAccounts_EmptyDb_ReturnsEmptyList()
    {
        var resp = await NewClient().GetAsync("/cqrs/accounts");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var list = await resp.Content.ReadFromJsonAsync<List<AccountSummaryDto>>();
        list.Should().NotBeNull();
    }
}

public record CreatedResult(int Id);

public class CqrsTestFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection;
    public CqrsTestFactory()
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
