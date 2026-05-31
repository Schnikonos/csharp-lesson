using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Lesson.Data;
using Lesson.Entities;

namespace Lesson.Tests;

/// <summary>
/// Lesson 12-C — Integration tests with WebApplicationFactory + in-memory SQLite.
///
/// Integration tests exercise the FULL request pipeline:
///   HTTP request → routing → controller → EF Core → SQLite → JSON response
///
/// Key techniques:
///   - WebApplicationFactory&lt;Program&gt;  replaces the real host for tests
///   - Persistent in-memory SQLite connection  keeps data across test calls
///   - IServiceScope  lets you seed or verify the DB directly in tests
///   - IClassFixture&lt;T&gt;  shares one factory instance across all tests in the class
///
/// Java parallel:
///   @SpringBootTest + MockMvc → WebApplicationFactory + HttpClient
///   @Transactional rollback   → per-test SQLite DB (isolated via new connection per factory)
/// </summary>
public class AccountsIntegrationTests : IClassFixture<AccountsTestFactory>
{
    private readonly HttpClient _client;
    private readonly AccountsTestFactory _factory;

    public AccountsIntegrationTests(AccountsTestFactory factory)
    {
        _factory = factory;
        _client  = factory.CreateClient();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static CreateAccountRequest NewRequest(string number = "INT-001") =>
        new(number, "IntegrationTestOwner", "Savings", 0m);

    private async Task<int> CreateAccountAsync(string number = "INT-001")
    {
        var response = await _client.PostAsJsonAsync("/accounts", NewRequest(number));
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<AccountResponse>();
        return created!.Id;
    }

    // ── CREATE ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task POST_Accounts_CreatesAccount_AndReturns201()
    {
        var response = await _client.PostAsJsonAsync("/accounts", NewRequest("INT-C01"));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<AccountResponse>();
        body.Should().NotBeNull();
        body!.AccountNumber.Should().Be("INT-C01");
        body.OwnerName.Should().Be("IntegrationTestOwner");
    }

    [Fact]
    public async Task POST_Accounts_DuplicateNumber_Returns409()
    {
        await CreateAccountAsync("INT-DUP");
        var response = await _client.PostAsJsonAsync("/accounts", NewRequest("INT-DUP"));
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ── READ ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GET_Accounts_ById_Returns200_WithCorrectData()
    {
        var id = await CreateAccountAsync("INT-R01");
        var response = await _client.GetAsync($"/accounts/{id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<AccountResponse>();
        body!.Id.Should().Be(id);
        body.AccountNumber.Should().Be("INT-R01");
    }

    [Fact]
    public async Task GET_Accounts_ById_NotFound_Returns404()
    {
        var response = await _client.GetAsync("/accounts/999999");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── UPDATE ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PUT_Accounts_UpdatesBalance()
    {
        var id       = await CreateAccountAsync("INT-U01");
        var update   = new UpdateAccountRequest("INT-U01", "Updated Owner", "Checking", 500m);
        var response = await _client.PutAsJsonAsync($"/accounts/{id}", update);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<AccountResponse>();
        body!.Balance.Should().Be(500m);
        body.OwnerName.Should().Be("Updated Owner");
    }

    // ── DELETE (soft) ─────────────────────────────────────────────────────────

    [Fact]
    public async Task DELETE_Account_SoftDeletes_AndHidesFromList()
    {
        var id = await CreateAccountAsync("INT-D01");

        var deleteResp = await _client.DeleteAsync($"/accounts/{id}");
        deleteResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // After soft-delete the record should no longer appear in the main list
        var getResp = await _client.GetAsync($"/accounts/{id}");
        getResp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Database seeding via IServiceScope ────────────────────────────────────

    [Fact]
    public async Task DirectDbSeed_CanBeReadViaApi()
    {
        // Seed directly into the DB — no HTTP POST needed
        using (var scope = _factory.Services.CreateScope())
        {
            var db      = scope.ServiceProvider.GetRequiredService<BankingDbContext>();
            var seeded  = new BankAccount
            {
                AccountNumber = "SEED-001",
                OwnerName     = "Seeded User",
                AccountType   = "Checking",
                Balance       = 9_999m,
                IsActive      = true
            };
            db.BankAccounts.Add(seeded);
            await db.SaveChangesAsync();
        }

        // Verify via API
        var list = await _client.GetFromJsonAsync<List<AccountResponse>>("/accounts");
        list.Should().Contain(a => a.AccountNumber == "SEED-001");
    }
}

// ── Shared DTO record shapes (mirror what the API returns) ────────────────────
internal record AccountResponse(int Id, string AccountNumber, string OwnerName,
    string AccountType, decimal Balance, bool IsActive);
internal record CreateAccountRequest(string AccountNumber, string OwnerName,
    string AccountType, decimal InitialBalance);
internal record UpdateAccountRequest(string AccountNumber, string OwnerName,
    string AccountType, decimal Balance);

// ── WebApplicationFactory ─────────────────────────────────────────────────────
public class AccountsTestFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection;
    public AccountsTestFactory()
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
