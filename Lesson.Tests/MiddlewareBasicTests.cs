using System.Net;
using System.Net.Http.Json;
using Lesson.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lesson.Tests;

/// <summary>
/// Lesson 06-A integration tests — Custom Middleware
///
/// Verifies:
///   • ResponseHeaderMiddleware injects X-Powered-By on every response
///   • RequestLoggingMiddleware does not break the pipeline (smoke test)
///   • Middleware is transparent — response body and status are unaffected
/// </summary>
public class MiddlewareBasicTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly SqliteConnection _connection;
    private readonly HttpClient _client;

    public MiddlewareBasicTests(WebApplicationFactory<Program> factory)
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

    // ── ResponseHeaderMiddleware ───────────────────────────────────────────────

    [Fact]
    public async Task Ping_ResponseContainsXPoweredByHeader()
    {
        var response = await _client.GetAsync("/middleware/ping");
        Assert.True(response.Headers.Contains("X-Powered-By"));
    }

    [Fact]
    public async Task Ping_XPoweredByHeader_ContainsExpectedValue()
    {
        var response = await _client.GetAsync("/middleware/ping");
        var value = response.Headers.GetValues("X-Powered-By").First();
        Assert.Contains("ASP.NET Core", value);
    }

    [Fact]
    public async Task AnyEndpoint_ResponseContainsXPoweredByHeader()
    {
        // Middleware should apply to ALL routes, not just /middleware/*
        var response = await _client.GetAsync("/linq/products");
        Assert.True(response.Headers.Contains("X-Powered-By"));
    }

    // ── RequestLoggingMiddleware (pipeline transparency) ──────────────────────

    [Fact]
    public async Task Ping_Returns200_MiddlewareDoesNotBreakPipeline()
    {
        var response = await _client.GetAsync("/middleware/ping");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Ping_ResponseBody_IsCorrect()
    {
        var body = await _client.GetFromJsonAsync<PingResponse>("/middleware/ping");
        Assert.NotNull(body);
        Assert.Equal("pong", body.Message);
    }

    [Fact]
    public async Task Slow_Returns200_AfterDelay()
    {
        var response = await _client.GetAsync("/middleware/slow");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task UnknownRoute_Returns404_MiddlewareStillAddsHeader()
    {
        var response = await _client.GetAsync("/does-not-exist");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.True(response.Headers.Contains("X-Powered-By"));
    }

    private record PingResponse(string Message);
}
