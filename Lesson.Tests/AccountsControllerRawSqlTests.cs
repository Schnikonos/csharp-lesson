using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lesson.Controllers;
using Lesson.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lesson.Tests;

/// <summary>
/// Lesson 04-C integration tests — exercises:
///   • FromSqlRaw (parameterised raw SQL)
///   • Stored-procedure simulation (FromSqlRaw with SP-style call)
///   • Compiled query (EF.CompileAsyncQuery)
///   • AsSplitQuery (cartesian explosion prevention)
///
/// The test harness spins up the real ASP.NET Core pipeline against an in-memory
/// SQLite database (kept alive for the lifetime of the test class via a shared
/// SqliteConnection) and applies all EF Core migrations so the full schema
/// including seed data is available.
/// </summary>
public class AccountsControllerRawSqlTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly SqliteConnection _connection;
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public AccountsControllerRawSqlTests(WebApplicationFactory<Program> factory)
    {
        // Keep a single SQLite in-memory connection open for the lifetime of the
        // test class so migrations and seed data are available to all tests.
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _factory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                // Remove the real DbContext registration and replace it with
                // one backed by our shared in-memory SQLite connection.
                var descriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(DbContextOptions<BankingDbContext>));
                if (descriptor is not null)
                    services.Remove(descriptor);

                services.AddDbContext<BankingDbContext>(options =>
                    options.UseSqlite(_connection));

                // Apply migrations (+ seed data) once at startup.
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

    // ── FromSqlRaw ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetByRawSql_BelowAllBalances_ReturnsAllAccounts()
    {
        var response = await _client.GetAsync("/accounts/raw?minBalance=0");
        response.EnsureSuccessStatusCode();
        var accounts = await response.Content.ReadFromJsonAsync<List<AccountResponse>>(_json);
        Assert.NotNull(accounts);
        Assert.NotEmpty(accounts);
    }

    [Fact]
    public async Task GetByRawSql_AboveAllBalances_ReturnsEmpty()
    {
        var response = await _client.GetAsync("/accounts/raw?minBalance=9999999");
        response.EnsureSuccessStatusCode();
        var accounts = await response.Content.ReadFromJsonAsync<List<AccountResponse>>(_json);
        Assert.NotNull(accounts);
        Assert.Empty(accounts);
    }

    [Fact]
    public async Task GetByRawSql_OnlyReturnsAccountsAboveThreshold()
    {
        // ACC-0001 balance = 12 500, ACC-0002 = 45 000  (from seed data)
        var response = await _client.GetAsync("/accounts/raw?minBalance=20000");
        response.EnsureSuccessStatusCode();
        var accounts = await response.Content.ReadFromJsonAsync<List<AccountResponse>>(_json);
        Assert.NotNull(accounts);
        Assert.All(accounts, a => Assert.True(a.Balance > 20_000));
    }

    // ── Stored procedure simulation ───────────────────────────────────────────

    [Fact]
    public async Task GetByNumberStoredProc_ExistingAccount_ReturnsAccount()
    {
        var response = await _client.GetAsync("/accounts/by-number-sp/ACC-0001");
        response.EnsureSuccessStatusCode();
        var account = await response.Content.ReadFromJsonAsync<AccountResponse>(_json);
        Assert.NotNull(account);
        Assert.Equal("ACC-0001", account.AccountNumber);
    }

    [Fact]
    public async Task GetByNumberStoredProc_UnknownAccount_ReturnsNotFound()
    {
        var response = await _client.GetAsync("/accounts/by-number-sp/UNKNOWN");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── Compiled query ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetByNumberCompiled_ExistingAccount_ReturnsAccount()
    {
        var response = await _client.GetAsync("/accounts/by-number-compiled/ACC-0002");
        response.EnsureSuccessStatusCode();
        var account = await response.Content.ReadFromJsonAsync<AccountResponse>(_json);
        Assert.NotNull(account);
        Assert.Equal("ACC-0002", account.AccountNumber);
    }

    [Fact]
    public async Task GetByNumberCompiled_UnknownAccount_ReturnsNotFound()
    {
        var response = await _client.GetAsync("/accounts/by-number-compiled/NOTEXIST");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetByNumberCompiled_CalledTwice_BothReturnSameResult()
    {
        // Compiled query must return consistent results on repeated calls.
        var r1 = await _client.GetAsync("/accounts/by-number-compiled/ACC-0001");
        var r2 = await _client.GetAsync("/accounts/by-number-compiled/ACC-0001");
        r1.EnsureSuccessStatusCode();
        r2.EnsureSuccessStatusCode();
        var a1 = await r1.Content.ReadFromJsonAsync<AccountResponse>(_json);
        var a2 = await r2.Content.ReadFromJsonAsync<AccountResponse>(_json);
        Assert.NotNull(a1);
        Assert.NotNull(a2);
        Assert.Equal(a1.Id, a2.Id);
    }

    // ── Split query / cartesian explosion ─────────────────────────────────────

    [Fact]
    public async Task GetWithTransactions_ReturnsAccountsWithTransactions()
    {
        var response = await _client.GetAsync("/accounts/with-transactions");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var accounts = doc.RootElement.EnumerateArray().ToList();
        Assert.NotEmpty(accounts);
    }

    [Fact]
    public async Task GetWithTransactions_SeededTransactions_ArePresent()
    {
        var response = await _client.GetAsync("/accounts/with-transactions");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        // At least one account should have transactions from seed data.
        var totalTransactions = doc.RootElement
            .EnumerateArray()
            .Sum(a => a.GetProperty("transactions").GetArrayLength());
        Assert.True(totalTransactions > 0,
            "Expected seeded transactions to be present in split-query result.");
    }
}
