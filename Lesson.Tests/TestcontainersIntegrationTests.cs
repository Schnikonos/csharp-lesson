using System.Net;
using System.Net.Http.Json;
using DotNet.Testcontainers.Builders;
using FluentAssertions;
using Lesson.Data;
using Lesson.Entities;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace Lesson.Tests;

// =============================================================================
// LESSON 12-C EXTENDED: Testcontainers — real PostgreSQL in integration tests
//
// WHY TESTCONTAINERS?
//   SQLite in-memory is convenient but it is not PostgreSQL. SQL dialects differ,
//   JSON operators, generated columns, and constraint behaviour can vary.
//   Testcontainers spins up a real PostgreSQL Docker container for the test run
//   and tears it down automatically — no Docker Compose, no shared state.
//
// PATTERN
//   IAsyncLifetime   → start/stop the container once per class (fast)
//   IClassFixture    → share one factory instance across all tests in the class
//
// Java parallel:
//   @Testcontainers + @Container PostgreSQLContainer in JUnit 5
//   @DynamicPropertySource to set spring.datasource.url
//   → here we swap out the EF Core DbContextOptions in WebApplicationFactory
//
// PREREQUISITES
//   Docker must be running on the build machine.
//   The test is decorated with [Trait("Category","Docker")] so it can be
//   excluded in environments without Docker:
//     dotnet test --filter "Category!=Docker"
// =============================================================================

[Trait("Category", "Docker")]
public class TestcontainersIntegrationTests : IAsyncLifetime
{
    // ─────────────────────────────────────────────────────────────────────────
    // Container definition
    //
    // PostgreSqlBuilder uses the official postgres:16-alpine image by default.
    // WithCleanUp(true) removes the container even on test-host crash.
    // ─────────────────────────────────────────────────────────────────────────
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("banktest")
        .WithUsername("test")
        .WithPassword("test")
        .WithCleanUp(true)
        .Build();

    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;

    // =========================================================================
    // IAsyncLifetime — start container + build factory before any test runs
    // =========================================================================
    public async Task InitializeAsync()
    {
        // 1. Start the container — blocks until the PostgreSQL port is ready.
        await _postgres.StartAsync();

        // 2. Build the WebApplicationFactory with the Postgres connection string.
        //    We replace the default SQLite DbContext with Npgsql pointing at the
        //    container, then run EF Core migrations so the schema is ready.
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(host =>
                host.ConfigureServices(services =>
                {
                    // Remove the existing DbContextOptions registration
                    var descriptor = services.SingleOrDefault(
                        d => d.ServiceType == typeof(DbContextOptions<BankingDbContext>));
                    if (descriptor is not null) services.Remove(descriptor);

                    // Register Npgsql against the running container
                    services.AddDbContext<BankingDbContext>(opts =>
                        opts.UseNpgsql(_postgres.GetConnectionString()));
                }));

        // 3. Run EF Core migrations against the fresh container database.
        //    Java parallel: Flyway.migrate() called in @BeforeAll
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BankingDbContext>();
        await db.Database.MigrateAsync();

        _client = _factory.CreateClient();
    }

    // =========================================================================
    // IAsyncLifetime — stop and remove the container after all tests complete
    // =========================================================================
    public async Task DisposeAsync()
    {
        if (_factory is not null) await _factory.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    // =========================================================================
    // TEST 1 — Create a bank account, then retrieve it (full round-trip)
    //
    // This exercises the real PostgreSQL write path including constraint checks
    // that may behave differently to SQLite.
    // =========================================================================
    [Fact]
    public async Task CreateAccount_ThenGetById_ReturnsAccount()
    {
        // Arrange
        var request = new CreateAccountRequest("TC-001", "Testcontainers Owner", "Savings", 100m);

        // Act — POST
        var postResponse = await _client!.PostAsJsonAsync("/accounts", request);
        postResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await postResponse.Content.ReadFromJsonAsync<AccountResponse>();
        created.Should().NotBeNull();
        created!.Id.Should().BeGreaterThan(0);

        // Act — GET
        var getResponse = await _client.GetAsync($"/accounts/{created.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var fetched = await getResponse.Content.ReadFromJsonAsync<AccountResponse>();

        // Assert
        fetched!.AccountNumber.Should().Be("TC-001");
        fetched.OwnerName.Should().Be("Testcontainers Owner");
    }

    // =========================================================================
    // TEST 2 — Duplicate account number → 409 Conflict (PostgreSQL unique index)
    //
    // SQLite and PostgreSQL both enforce unique constraints, but this test proves
    // that the real DB behaviour matches our expectations.
    // =========================================================================
    [Fact]
    public async Task CreateAccount_DuplicateNumber_Returns409()
    {
        var request = new CreateAccountRequest("TC-DUP", "Alice", "Checking", 0m);

        var first  = await _client!.PostAsJsonAsync("/accounts", request);
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        var second = await _client.PostAsJsonAsync("/accounts", request);
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // =========================================================================
    // TEST 3 — List accounts includes the seeded record
    //
    // Validates that GET /accounts returns all persisted rows from PostgreSQL,
    // not just an in-memory projection.
    // =========================================================================
    [Fact]
    public async Task GetAccounts_ReturnsPersistedRows()
    {
        // Seed directly via EF Core to bypass API validation
        using var scope = _factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BankingDbContext>();
        db.BankAccounts.Add(new BankAccount
        {
            AccountNumber = "TC-SEED",
            OwnerName     = "Seed User",
            AccountType   = "Savings",
            Balance       = 500m
        });
        await db.SaveChangesAsync();

        // Fetch via HTTP
        var response = await _client!.GetAsync("/accounts");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var accounts = await response.Content.ReadFromJsonAsync<List<AccountResponse>>();
        accounts.Should().Contain(a => a.AccountNumber == "TC-SEED");
    }
}
