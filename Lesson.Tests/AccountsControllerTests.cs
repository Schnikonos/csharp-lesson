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
/// Integration tests for Lesson 03-A: EF Core CRUD via AccountsController.
///
/// Strategy:
///   - Open a single SqliteConnection per test and keep it alive for the
///     entire test duration. SQLite in-memory databases are tied to the
///     connection lifetime — closing all connections wipes the DB.
///   - Pass that same open connection to AddDbContext so all scopes
///     (startup MigrateAsync + request handlers) share the same DB.
///   - WebApplicationFactory starts the full ASP.NET Core pipeline.
/// </summary>
public class AccountsControllerTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly SqliteConnection _connection;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public AccountsControllerTests()
    {
        // Keep one connection open — in-memory DB lives as long as this connection.
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
            host.ConfigureServices(services =>
            {
                // Replace the real SQLite registration with our shared in-memory connection.
                var descriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(DbContextOptions<BankingDbContext>));
                if (descriptor is not null) services.Remove(descriptor);

                services.AddDbContext<BankingDbContext>(options =>
                    options.UseSqlite(_connection));
            }));

        // CreateClient() triggers host startup → MigrateAsync() runs on our in-memory DB.
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
        _connection.Dispose();
    }

    // -------------------------------------------------------------------------
    // GET /accounts — list
    // -------------------------------------------------------------------------
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

    // -------------------------------------------------------------------------
    // GET /accounts/{id} — found
    // -------------------------------------------------------------------------
    [Fact]
    public async Task GetById_ExistingId_ReturnsAccount()
    {
        var response = await _client.GetAsync("/accounts/1");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<AccountResponse>(JsonOpts);
        body!.AccountNumber.Should().Be("ACC-0001");
        body.OwnerName.Should().Be("Alice Dupont");
    }

    // -------------------------------------------------------------------------
    // GET /accounts/{id} — not found
    // -------------------------------------------------------------------------
    [Fact]
    public async Task GetById_MissingId_ReturnsNotFound()
    {
        var response = await _client.GetAsync("/accounts/9999");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // -------------------------------------------------------------------------
    // POST /accounts — create
    // -------------------------------------------------------------------------
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

    // -------------------------------------------------------------------------
    // POST /accounts — duplicate account number
    // -------------------------------------------------------------------------
    [Fact]
    public async Task Create_DuplicateAccountNumber_ReturnsConflict()
    {
        var request = new CreateAccountRequest("ACC-0001", "Duplicate Owner", "Checking", 0m);
        var response = await _client.PostAsJsonAsync("/accounts", request);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // -------------------------------------------------------------------------
    // PUT /accounts/{id} — update
    // -------------------------------------------------------------------------
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

    // -------------------------------------------------------------------------
    // DELETE /accounts/{id}
    // -------------------------------------------------------------------------
    [Fact]
    public async Task Delete_ExistingAccount_ReturnsNoContent()
    {
        // First create a fresh account so delete doesn't affect other tests
        var create = await _client.PostAsJsonAsync("/accounts",
            new CreateAccountRequest("ACC-DEL-001", "To Delete", "Checking", 0m));
        var created = await create.Content.ReadFromJsonAsync<AccountResponse>(JsonOpts);

        var deleteResponse = await _client.DeleteAsync($"/accounts/{created!.Id}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var getResponse = await _client.GetAsync($"/accounts/{created.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
