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
/// Lesson 03-B — integration tests for AccountsController.
///
/// Covers 03-A CRUD + 03-B additions:
///   - GET /accounts?type= filter (IQueryable composition)
///   - Address (owned entity) round-trip on create and update
///
/// Test strategy: one shared SqliteConnection per test class instance keeps
/// the in-memory database alive across startup MigrateAsync() and HTTP requests.
/// </summary>
public class AccountsControllerTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly SqliteConnection _connection;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public AccountsControllerTests()
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

        // Triggers app startup → MigrateAsync() seeds the in-memory DB.
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
        _connection.Dispose();
    }

    // ─── 03-A: basic CRUD ────────────────────────────────────────────────────

    [Fact]
    public async Task GetAll_ReturnsSeededAccounts()
    {
        var response = await _client.GetAsync("/accounts");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<List<AccountResponse>>(JsonOpts);
        body.Should().NotBeNull();
        body!.Count.Should().BeGreaterThanOrEqualTo(2);
        body.Should().Contain(a => a.AccountNumber == "ACC-0001");
    }

    [Fact]
    public async Task GetById_ExistingId_ReturnsAccount()
    {
        var response = await _client.GetAsync("/accounts/1");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<AccountResponse>(JsonOpts);
        body!.AccountNumber.Should().Be("ACC-0001");
        body.OwnerName.Should().Be("Alice Dupont");
    }

    [Fact]
    public async Task GetById_MissingId_ReturnsNotFound()
    {
        var response = await _client.GetAsync("/accounts/9999");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Create_ValidRequest_ReturnsCreated()
    {
        var request = new CreateAccountRequest("ACC-TEST-001", "Charlie Brown", "Savings", 1000m);
        var response = await _client.PostAsJsonAsync("/accounts", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();

        var body = await response.Content.ReadFromJsonAsync<AccountResponse>(JsonOpts);
        body!.AccountNumber.Should().Be("ACC-TEST-001");
        body.Balance.Should().Be(1000m);
        body.Id.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Create_DuplicateAccountNumber_ReturnsConflict()
    {
        var request = new CreateAccountRequest("ACC-0001", "Duplicate Owner", "Checking", 0m);
        var response = await _client.PostAsJsonAsync("/accounts", request);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Update_ExistingAccount_ReturnsUpdatedData()
    {
        var request = new UpdateAccountRequest("Alice Updated", "Savings", 99999m, true);
        var response = await _client.PutAsJsonAsync("/accounts/1", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<AccountResponse>(JsonOpts);
        body!.OwnerName.Should().Be("Alice Updated");
        body.AccountType.Should().Be("Savings");
        body.Balance.Should().Be(99999m);
    }

    [Fact]
    public async Task Delete_ExistingAccount_ReturnsNoContent()
    {
        // Create a dedicated account so this test is independent.
        var create = await _client.PostAsJsonAsync("/accounts",
            new CreateAccountRequest("ACC-DEL-001", "To Delete", "Checking", 0m));
        var created = await create.Content.ReadFromJsonAsync<AccountResponse>(JsonOpts);

        var deleteResponse = await _client.DeleteAsync($"/accounts/{created!.Id}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var getResponse = await _client.GetAsync($"/accounts/{created.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ─── 03-B: IQueryable filter ─────────────────────────────────────────────

    /// <summary>
    /// Demonstrates IQueryable composition: the type filter becomes a SQL WHERE clause,
    /// not an in-memory loop. Only matching rows are fetched from the database.
    /// </summary>
    [Fact]
    public async Task GetAll_FilterByType_ReturnsOnlyMatchingAccounts()
    {
        // Seed both types exist (ACC-0001 = Checking, ACC-0002 = Savings).
        var response = await _client.GetAsync("/accounts?type=Savings");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<List<AccountResponse>>(JsonOpts);
        body.Should().NotBeNull();
        body!.Should().OnlyContain(a => a.AccountType == "Savings");
        body.Should().NotContain(a => a.AccountType == "Checking");
    }

    [Fact]
    public async Task GetAll_FilterByType_UnknownType_ReturnsEmptyList()
    {
        var response = await _client.GetAsync("/accounts?type=Unknown");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<List<AccountResponse>>(JsonOpts);
        body.Should().NotBeNull().And.BeEmpty();
    }

    // ─── 03-B: Owned entity (Address) ────────────────────────────────────────

    /// <summary>
    /// Creates an account with an Address and verifies it round-trips through the API.
    /// The address columns are stored in the BankAccounts table (no join needed).
    /// </summary>
    [Fact]
    public async Task Create_WithAddress_AddressRoundTrips()
    {
        var address = new AddressDto("12 Rue de la Paix", "Paris", "75001", "France");
        var request = new CreateAccountRequest("ACC-ADDR-001", "Marie Curie", "Savings", 5000m, address);

        var response = await _client.PostAsJsonAsync("/accounts", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<AccountResponse>(JsonOpts);
        body!.Address.Should().NotBeNull();
        body.Address!.Street.Should().Be("12 Rue de la Paix");
        body.Address.City.Should().Be("Paris");
        body.Address.PostalCode.Should().Be("75001");
        body.Address.Country.Should().Be("France");
    }

    [Fact]
    public async Task Update_WithAddress_AddressIsPersisted()
    {
        // First create without address, then update to add one.
        var create = await _client.PostAsJsonAsync("/accounts",
            new CreateAccountRequest("ACC-ADDR-002", "Test User", "Checking", 0m));
        var created = await create.Content.ReadFromJsonAsync<AccountResponse>(JsonOpts);

        var updateRequest = new UpdateAccountRequest(
            "Test User",
            "Checking",
            0m,
            true,
            new AddressDto("1 Main St", "Lyon", "69001", "France"));

        var update = await _client.PutAsJsonAsync($"/accounts/{created!.Id}", updateRequest);

        update.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await update.Content.ReadFromJsonAsync<AccountResponse>(JsonOpts);
        body!.Address.Should().NotBeNull();
        body.Address!.City.Should().Be("Lyon");
    }

    [Fact]
    public async Task Create_WithoutAddress_AddressIsNull()
    {
        var request = new CreateAccountRequest("ACC-NOADDR-001", "No Address User", "Checking", 100m);
        var response = await _client.PostAsJsonAsync("/accounts", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<AccountResponse>(JsonOpts);
        body!.Address.Should().BeNull();
    }

    // ─── 03-C: Audit fields ───────────────────────────────────────────────────

    /// <summary>
    /// SaveChangesAsync override stamps UpdatedAt/UpdatedBy on every Add/Modify.
    /// Java parallel: @PrePersist / @LastModifiedDate lifecycle hooks.
    /// </summary>
    [Fact]
    public async Task Create_SetsAuditFields()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        var response = await _client.PostAsJsonAsync("/accounts",
            new CreateAccountRequest("ACC-AUDIT-001", "Audit User", "Checking", 0m));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<AccountResponse>(JsonOpts);
        body!.UpdatedAt.Should().BeAfter(before);
        body.UpdatedBy.Should().Be("system");
    }

    // ─── 03-C: Soft delete + restore ─────────────────────────────────────────

    /// <summary>
    /// DELETE soft-deletes the row; subsequent GET returns 404 (hidden by global filter).
    /// GET /accounts/deleted exposes the soft-deleted row.
    /// Java parallel: @SQLDelete + @Where on the entity class.
    /// </summary>
    [Fact]
    public async Task Delete_SoftDeletes_HiddenFromGetAll()
    {
        var create = await _client.PostAsJsonAsync("/accounts",
            new CreateAccountRequest("ACC-SOFT-001", "Soft Delete User", "Checking", 0m));
        var created = await create.Content.ReadFromJsonAsync<AccountResponse>(JsonOpts);

        var deleteResp = await _client.DeleteAsync($"/accounts/{created!.Id}");
        deleteResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Global filter hides the row from regular queries.
        var getResp = await _client.GetAsync($"/accounts/{created.Id}");
        getResp.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Row still exists in the deleted list (bypasses global filter).
        var deletedResp = await _client.GetAsync("/accounts/deleted");
        deletedResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var deletedList = await deletedResp.Content.ReadFromJsonAsync<List<AccountResponse>>(JsonOpts);
        deletedList.Should().Contain(a => a.Id == created.Id);
    }

    [Fact]
    public async Task Restore_MakesAccountVisibleAgain()
    {
        var create = await _client.PostAsJsonAsync("/accounts",
            new CreateAccountRequest("ACC-RESTORE-001", "Restore User", "Checking", 0m));
        var created = await create.Content.ReadFromJsonAsync<AccountResponse>(JsonOpts);
        await _client.DeleteAsync($"/accounts/{created!.Id}");

        var restoreResp = await _client.PostAsync($"/accounts/{created.Id}/restore", null);
        restoreResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var getResp = await _client.GetAsync($"/accounts/{created.Id}");
        getResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
