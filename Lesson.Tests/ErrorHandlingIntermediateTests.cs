using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lesson.Data;
using Lesson.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lesson.Tests;

/// <summary>
/// Lesson 07-B integration tests — Global Exception Handler, ProblemDetails, FluentValidation
///
/// Verifies:
///   • Unhandled exception → GlobalExceptionHandler → 500 ProblemDetails
///   • ProblemDetails body has required fields (status, title, detail)
///   • FluentValidation endpoint rejects invalid input with 400 ValidationProblemDetails
///   • FluentValidation cross-property rule (from == to) produces correct error
///   • Valid payload passes FluentValidation and returns 200
/// </summary>
public class ErrorHandlingIntermediateTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly SqliteConnection _connection;
    private readonly HttpClient _client;

    public ErrorHandlingIntermediateTests(WebApplicationFactory<Program> factory)
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

    // ── GlobalExceptionHandler + ProblemDetails ───────────────────────────────

    [Fact]
    public async Task UnhandledException_Returns500()
    {
        var response = await _client.GetAsync("/error-demo/unhandled");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task UnhandledException_ResponseIsProblemDetails()
    {
        var response = await _client.GetAsync("/error-demo/unhandled");
        var body = await response.Content.ReadFromJsonAsync<ProblemDetailsBody>();
        Assert.Equal(500, body?.Status);
    }

    [Fact]
    public async Task UnhandledException_ProblemDetails_HasTitle()
    {
        var response = await _client.GetAsync("/error-demo/unhandled");
        var body = await response.Content.ReadFromJsonAsync<ProblemDetailsBody>();
        Assert.False(string.IsNullOrWhiteSpace(body?.Title));
    }

    [Fact]
    public async Task UnhandledException_ProblemDetails_DetailContainsExceptionMessage()
    {
        var response = await _client.GetAsync("/error-demo/unhandled");
        var body = await response.Content.ReadFromJsonAsync<ProblemDetailsBody>();
        Assert.Contains("not caught", body?.Detail ?? "");
    }

    // ── FluentValidation ─────────────────────────────────────────────────────

    [Fact]
    public async Task FluentValidate_WhenAmountTooLow_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/error-demo/fluent-validate",
            new { fromAccount = "ACC001", toAccount = "ACC002", amount = 0m });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task FluentValidate_WhenSameAccount_Returns400WithCrossPropertyError()
    {
        var response = await _client.PostAsJsonAsync("/error-demo/fluent-validate",
            new { fromAccount = "ACC001", toAccount = "ACC001", amount = 100m });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ValidationProblemBody>();
        Assert.True(body?.Errors?.ContainsKey("ToAccount") == true);
    }

    [Fact]
    public async Task FluentValidate_WhenAccountTooShort_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/error-demo/fluent-validate",
            new { fromAccount = "AB", toAccount = "ACC002", amount = 100m });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task FluentValidate_WithValidPayload_Returns200()
    {
        var response = await _client.PostAsJsonAsync("/error-demo/fluent-validate",
            new { fromAccount = "ACC001", toAccount = "ACC002", amount = 500m });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task FluentValidate_WithValidPayload_ResponseContainsAmount()
    {
        var response = await _client.PostAsJsonAsync("/error-demo/fluent-validate",
            new { fromAccount = "ACC001", toAccount = "ACC002", amount = 500m });
        var body = await response.Content.ReadFromJsonAsync<ValidBody>();
        Assert.Equal(500m, body?.Amount);
    }

    private record ProblemDetailsBody(int? Status, string? Title, string? Detail);
    private record ValidationProblemBody(Dictionary<string, string[]>? Errors);
    private record ValidBody(decimal Amount);
}
