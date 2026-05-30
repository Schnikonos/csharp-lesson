using System.Net;
using System.Net.Http.Json;
using Lesson.Commands;
using Lesson.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lesson.Tests;

/// <summary>
/// Lesson 07-C integration tests — Domain Exception Hierarchy &amp; MediatR Validation Pipeline
///
/// Verifies:
///   • NotFoundException → DomainExceptionHandler → 404 ProblemDetails
///   • BusinessRuleException → DomainExceptionHandler → 422 ProblemDetails
///   • ForbiddenException → DomainExceptionHandler → 403 ProblemDetails
///   • MediatR ValidationBehavior → ValidationException → 400 on invalid command
///   • Valid MediatR command → handler → 201
///   • Cross-property rule (same account) fails validation via MediatR pipeline
/// </summary>
public class ErrorHandlingAdvancedTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly SqliteConnection _connection;
    private readonly HttpClient _client;

    public ErrorHandlingAdvancedTests(WebApplicationFactory<Program> factory)
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

    // ── DomainExceptionHandler ─────────────────────────────────────────────────

    [Fact]
    public async Task Get_PaymentNotFound_Returns404()
    {
        var response = await _client.GetAsync($"/payments/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_NotFound_ResponseIsProblemDetails()
    {
        var response = await _client.GetAsync($"/payments/{Guid.NewGuid()}");
        var body = await response.Content.ReadFromJsonAsync<ProblemBody>();
        Assert.Equal(404, body?.Status);
    }

    [Fact]
    public async Task Forbidden_Returns403()
    {
        var response = await _client.GetAsync("/payments/forbidden");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Forbidden_ResponseIsProblemDetails()
    {
        var response = await _client.GetAsync("/payments/forbidden");
        var body = await response.Content.ReadFromJsonAsync<ProblemBody>();
        Assert.Equal(403, body?.Status);
    }

    // ── MediatR validation pipeline ───────────────────────────────────────────

    [Fact]
    public async Task Create_WhenAmountZero_Returns400ViaMediatRPipeline()
    {
        var response = await _client.PostAsJsonAsync("/payments",
            new CreatePaymentCommand("ACC001", "ACC002", 0m));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WhenSameAccount_Returns400WithValidationError()
    {
        var response = await _client.PostAsJsonAsync("/payments",
            new CreatePaymentCommand("ACC001", "ACC001", 100m));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WhenAccountBlocked_Returns422BusinessRule()
    {
        var response = await _client.PostAsJsonAsync("/payments",
            new CreatePaymentCommand("BLOCKED01", "ACC002", 100m));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithValidCommand_Returns201()
    {
        var response = await _client.PostAsJsonAsync("/payments",
            new CreatePaymentCommand("ACC001", "ACC002", 250m));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithValidCommand_ResponseContainsPaymentId()
    {
        var response = await _client.PostAsJsonAsync("/payments",
            new CreatePaymentCommand("ACC001", "ACC002", 250m));
        var body = await response.Content.ReadFromJsonAsync<PaymentBody>();
        Assert.NotEqual(Guid.Empty, body?.Id);
    }

    private record ProblemBody(int? Status, string? Title, string? Detail);
    private record PaymentBody(Guid Id, string FromAccount, string ToAccount, decimal Amount);
}
