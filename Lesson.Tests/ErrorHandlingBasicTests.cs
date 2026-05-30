using System.Net;
using System.Net.Http.Json;
using Lesson.Data;
using Lesson.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lesson.Tests;

/// <summary>
/// Lesson 07-A integration tests — Error Handling &amp; Data Annotations Validation
///
/// Verifies:
///   • [ApiController] returns 400 with validation details when annotations are violated
///   • Valid payload creates the transfer and returns 201
///   • Domain rule violation (same account) returns 400
///   • GET returns 404 for unknown transfer ID
///   • simulate-error endpoint returns 500 with error message
/// </summary>
public class ErrorHandlingBasicTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly SqliteConnection _connection;
    private readonly HttpClient _client;

    public ErrorHandlingBasicTests(WebApplicationFactory<Program> factory)
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

    private Task ResetAsync() => _client.DeleteAsync("/transfers/reset");

    // ── [ApiController] automatic ModelState validation ───────────────────────

    [Fact]
    public async Task Create_WhenAmountIsZero_Returns400()
    {
        await ResetAsync();
        var response = await _client.PostAsJsonAsync("/transfers",
            new { fromAccount = "ACC001", toAccount = "ACC002", amount = 0m });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WhenFromAccountMissing_Returns400WithErrors()
    {
        await ResetAsync();
        var response = await _client.PostAsJsonAsync("/transfers",
            new { toAccount = "ACC002", amount = 100m });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ValidationProblemBody>();
        Assert.NotNull(body?.Errors);
    }

    [Fact]
    public async Task Create_WhenAccountNumberTooShort_Returns400()
    {
        await ResetAsync();
        var response = await _client.PostAsJsonAsync("/transfers",
            new { fromAccount = "AB", toAccount = "ACC002", amount = 100m });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WhenDescriptionTooLong_Returns400()
    {
        await ResetAsync();
        var response = await _client.PostAsJsonAsync("/transfers",
            new { fromAccount = "ACC001", toAccount = "ACC002", amount = 100m,
                  description = new string('x', 201) });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── Domain rule validation ────────────────────────────────────────────────

    [Fact]
    public async Task Create_WhenSameAccount_Returns400WithDomainError()
    {
        await ResetAsync();
        var response = await _client.PostAsJsonAsync("/transfers",
            new { fromAccount = "ACC001", toAccount = "ACC001", amount = 100m });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── Happy path ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_WithValidPayload_Returns201()
    {
        await ResetAsync();
        var response = await _client.PostAsJsonAsync("/transfers",
            new { fromAccount = "ACC001", toAccount = "ACC002", amount = 250m });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    // ── GetById ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetById_WhenNotFound_Returns404()
    {
        var response = await _client.GetAsync("/transfers/9999");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetById_AfterCreate_Returns200()
    {
        await ResetAsync();
        await _client.PostAsJsonAsync("/transfers",
            new { fromAccount = "ACC001", toAccount = "ACC002", amount = 100m });

        var response = await _client.GetAsync("/transfers/0");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── try/catch error simulation ────────────────────────────────────────────

    [Fact]
    public async Task SimulateError_Returns500WithMessage()
    {
        var response = await _client.GetAsync("/transfers/simulate-error");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ErrorBody>();
        Assert.Equal("Simulated domain error.", body?.Error);
    }

    private record ValidationProblemBody(Dictionary<string, string[]>? Errors);
    private record ErrorBody(string? Error);
}
