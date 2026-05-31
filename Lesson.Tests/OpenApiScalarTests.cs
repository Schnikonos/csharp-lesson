using System.Net;
using System.Text.Json;
using FluentAssertions;
using Lesson.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lesson.Tests;

// =============================================================================
// LESSON 21-C TESTS: OpenAPI document + Scalar UI
//
// Three scenarios:
//   1. /openapi/v1.json is reachable and contains valid JSON.
//   2. The document title and version match what we registered in AddOpenApi().
//   3. The document contains paths under /accounts.
//   4. The Scalar UI HTML page is served at /scalar/v1.
//
// WHY TEST THE OPENAPI DOCUMENT?
//   Contract tests catch accidental breaking changes to the API surface:
//   renamed routes, removed operations, changed response schemas.
//   .NET parallel: Spring REST Docs / Springfox assertions.
//
// Java parallel:
//   MockMvc + andExpect(jsonPath("$.info.title").value("Banking API"))
//   swagger-request-validator integration tests
// =============================================================================

/// <summary>Shared fixture — starts the app once for all OpenAPI/Scalar tests.</summary>
public sealed class OpenApiFixture : IDisposable
{
    private readonly SqliteConnection _connection;
    public WebApplicationFactory<Program> Factory { get; }

    public OpenApiFixture()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        Factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(host =>
            {
                // Force Development so MapOpenApi() and MapScalarApiReference() are registered
                host.UseSetting("ASPNETCORE_ENVIRONMENT", "Development");
                host.ConfigureServices(services =>
                {
                    var descriptor = services.SingleOrDefault(
                        d => d.ServiceType == typeof(DbContextOptions<BankingDbContext>));
                    if (descriptor is not null) services.Remove(descriptor);
                    services.AddDbContext<BankingDbContext>(opts => opts.UseSqlite(_connection));
                });
            });
    }

    public void Dispose()
    {
        Factory.Dispose();
        _connection.Dispose();
    }
}

public class OpenApiScalarTests : IClassFixture<OpenApiFixture>
{
    private readonly HttpClient _client;

    public OpenApiScalarTests(OpenApiFixture fixture)
    {
        _client = fixture.Factory.CreateClient();
    }

    // =========================================================================
    // TEST 1 — /openapi/v1.json is reachable and valid JSON
    // =========================================================================
    [Fact]
    public async Task OpenApiDocument_IsReachableAndValidJson()
    {
        var response = await _client.GetAsync("/openapi/v1.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        var doc  = JsonDocument.Parse(body);
        doc.Should().NotBeNull();
    }

    // =========================================================================
    // TEST 2 — Title and version match the AddOpenApi registration
    // =========================================================================
    [Fact]
    public async Task OpenApiDocument_HasCorrectTitleAndVersion()
    {
        var response = await _client.GetAsync("/openapi/v1.json");
        var body     = await response.Content.ReadAsStringAsync();
        var doc      = JsonDocument.Parse(body);
        var info     = doc.RootElement.GetProperty("info");

        info.GetProperty("title").GetString().Should().Be("Banking API");
        info.GetProperty("version").GetString().Should().Be("v1");
    }

    // =========================================================================
    // TEST 3 — Document exposes at least one path under /accounts
    //
    // Protects against accidental removal of the accounts controller.
    // Java parallel: assertThat(swagger.getPaths()).containsKey("/accounts")
    // =========================================================================
    [Fact]
    public async Task OpenApiDocument_ContainsAccountsPaths()
    {
        var response = await _client.GetAsync("/openapi/v1.json");
        var body     = await response.Content.ReadAsStringAsync();
        var doc      = JsonDocument.Parse(body);

        doc.RootElement.TryGetProperty("paths", out var paths).Should().BeTrue(
            "the OpenAPI document must have a 'paths' section");

        var keys = paths.EnumerateObject().Select(p => p.Name).ToList();
        keys.Should().Contain(k => k.StartsWith("/accounts"),
            "accounts controller must appear in the spec");
    }

    // =========================================================================
    // TEST 4 — Scalar UI is served at /scalar/v1
    // =========================================================================
    [Fact]
    public async Task ScalarUi_IsReachable()
    {
        var response = await _client.GetAsync("/scalar/v1");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().StartWith("text/html");
    }
}
