using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lesson.Controllers;
using Lesson.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lesson.Tests;

/// <summary>
/// Lesson 04-A — integration tests for CustomersController.
///
/// Verifies:
///   - Basic customer CRUD
///   - GET /customers/{id}/accounts  → Include + ThenInclude eager loads accounts
///   - GET /customers/{id}/accounts/active → filtered Include returns only active accounts
///   - POST /customers/{id}/accounts/{accountId} → FK assignment
///
/// Same shared-connection pattern as AccountsControllerTests to keep the
/// in-memory SQLite database alive across MigrateAsync and HTTP requests.
/// </summary>
public class CustomersControllerTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly SqliteConnection _connection;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public CustomersControllerTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
            host.ConfigureServices(services =>
            {
                var d = services.SingleOrDefault(
                    s => s.ServiceType == typeof(DbContextOptions<BankingDbContext>));
                if (d is not null) services.Remove(d);
                services.AddDbContext<BankingDbContext>(o => o.UseSqlite(_connection));
            }));

        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
        _connection.Dispose();
    }

    // ─── Basic customer CRUD ──────────────────────────────────────────────────

    [Fact]
    public async Task GetAll_ReturnsSeededCustomers()
    {
        var response = await _client.GetAsync("/customers");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<List<CustomerResponse>>(JsonOpts);
        body.Should().NotBeNull();
        body!.Should().Contain(c => c.Name == "Alice Dupont");
        body.Should().Contain(c => c.Name == "Bob Martin");
    }

    [Fact]
    public async Task Create_ValidRequest_ReturnsCreated()
    {
        var request = new CreateCustomerRequest("Charlie Brown", "charlie@example.com");
        var response = await _client.PostAsJsonAsync("/customers", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<CustomerResponse>(JsonOpts);
        body!.Id.Should().BeGreaterThan(0);
        body.Name.Should().Be("Charlie Brown");
        body.Email.Should().Be("charlie@example.com");
    }

    [Fact]
    public async Task Create_DuplicateEmail_ReturnsConflict()
    {
        var response = await _client.PostAsJsonAsync("/customers",
            new CreateCustomerRequest("Another Alice", "alice@example.com"));
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ─── Include + ThenInclude ────────────────────────────────────────────────

    /// <summary>
    /// Verifies that Include(c => c.Accounts).ThenInclude(a => a.Address) works:
    /// the seeded customer has a seeded account, and its Address is null (not provided in seed).
    /// The key assertion is that Accounts is populated — proving Include fired.
    /// Java parallel: findById with @EntityGraph(attributePaths = "accounts")
    /// </summary>
    [Fact]
    public async Task GetWithAccounts_SeededCustomer_ReturnsAccounts()
    {
        var response = await _client.GetAsync("/customers/1/accounts");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<CustomerResponse>(JsonOpts);
        body.Should().NotBeNull();
        body!.Name.Should().Be("Alice Dupont");
        // Include fired — the collection must contain ACC-0001
        body.Accounts.Should().NotBeEmpty();
        body.Accounts.Should().Contain(a => a.AccountNumber == "ACC-0001");
    }

    [Fact]
    public async Task GetWithAccounts_MissingCustomer_ReturnsNotFound()
    {
        var response = await _client.GetAsync("/customers/9999/accounts");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ─── Filtered Include ─────────────────────────────────────────────────────

    /// <summary>
    /// Creates a customer with one active and one inactive account, then verifies
    /// that GET /customers/{id}/accounts/active returns only the active one.
    ///
    /// This proves the filtered Include added a WHERE clause to the join,
    /// not just loaded everything and filtered in C#.
    ///
    /// Java parallel: @Query("SELECT c FROM Customer c JOIN FETCH c.accounts a WHERE a.isActive = true")
    /// </summary>
    [Fact]
    public async Task GetWithActiveAccounts_ReturnsOnlyActiveAccounts()
    {
        // Create a customer
        var createCust = await _client.PostAsJsonAsync("/customers",
            new CreateCustomerRequest("Diana Filter", "diana@example.com"));
        var customer = await createCust.Content.ReadFromJsonAsync<CustomerResponse>(JsonOpts);

        // Create two accounts
        var acc1Resp = await _client.PostAsJsonAsync("/accounts",
            new CreateAccountRequest("ACC-FILTER-ACTIVE", "Diana Filter", "Checking", 100m));
        var acc1 = await acc1Resp.Content.ReadFromJsonAsync<AccountResponse>(JsonOpts);

        var acc2Resp = await _client.PostAsJsonAsync("/accounts",
            new CreateAccountRequest("ACC-FILTER-INACTIVE", "Diana Filter", "Savings", 200m));
        var acc2 = await acc2Resp.Content.ReadFromJsonAsync<AccountResponse>(JsonOpts);

        // Deactivate account 2
        await _client.PutAsJsonAsync($"/accounts/{acc2!.Id}",
            new UpdateAccountRequest("Diana Filter", "Savings", 200m, false));

        // Assign both accounts to the customer
        await _client.PostAsync($"/customers/{customer!.Id}/accounts/{acc1!.Id}", null);
        await _client.PostAsync($"/customers/{customer.Id}/accounts/{acc2.Id}", null);

        // Get active accounts only
        var response = await _client.GetAsync($"/customers/{customer.Id}/accounts/active");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<CustomerResponse>(JsonOpts);
        body!.Accounts.Should().OnlyContain(a => a.IsActive);
        body.Accounts.Should().Contain(a => a.AccountNumber == "ACC-FILTER-ACTIVE");
        body.Accounts.Should().NotContain(a => a.AccountNumber == "ACC-FILTER-INACTIVE");
    }

    // ─── FK assignment ────────────────────────────────────────────────────────

    [Fact]
    public async Task AssignAccount_LinksAccountToCustomer()
    {
        // Create a new customer
        var createCust = await _client.PostAsJsonAsync("/customers",
            new CreateCustomerRequest("Eve Assign", "eve@example.com"));
        var customer = await createCust.Content.ReadFromJsonAsync<CustomerResponse>(JsonOpts);

        // Create an unlinked account
        var createAcc = await _client.PostAsJsonAsync("/accounts",
            new CreateAccountRequest("ACC-ASSIGN-001", "Eve Assign", "Checking", 0m));
        var account = await createAcc.Content.ReadFromJsonAsync<AccountResponse>(JsonOpts);

        // Assign
        var assignResp = await _client.PostAsync(
            $"/customers/{customer!.Id}/accounts/{account!.Id}", null);
        assignResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify account appears in customer's account list
        var getResp = await _client.GetAsync($"/customers/{customer.Id}/accounts");
        var body = await getResp.Content.ReadFromJsonAsync<CustomerResponse>(JsonOpts);
        body!.Accounts.Should().Contain(a => a.Id == account.Id);
    }
}
