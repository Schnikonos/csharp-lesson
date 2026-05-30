using System.Net;
using System.Net.Http.Json;
using Lesson.Data;
using Lesson.Filters;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lesson.Tests;

/// <summary>
/// Lesson 06-C integration tests — Advanced Filters
///
/// Verifies:
///   • ResponseCacheFilter (IResourceFilter) short-circuits on second call
///   • EnvelopeResultFilter (IResultFilter) wraps the result in { data, meta }
///   • ApiKeyEndpointFilter (IEndpointFilter) returns 401 without key, 200 with correct key
/// </summary>
public class AdvancedFilterTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly SqliteConnection _connection;
    private readonly HttpClient _client;

    public AdvancedFilterTests(WebApplicationFactory<Program> factory)
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

    // ── ResponseCacheFilter (IResourceFilter) ────────────────────────────────

    [Fact]
    public async Task Cached_FirstCall_Returns200()
    {
        await _client.DeleteAsync("/advanced-filters/reset-cache");
        ResponseCacheFilter.ClearCache();

        var response = await _client.GetAsync("/advanced-filters/cached");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Cached_SecondCall_ReturnsCachedResult()
    {
        await _client.DeleteAsync("/advanced-filters/reset-cache");
        ResponseCacheFilter.ClearCache();

        var first  = await (await _client.GetAsync("/advanced-filters/cached"))
                           .Content.ReadFromJsonAsync<CachedBody>();
        var second = await (await _client.GetAsync("/advanced-filters/cached"))
                           .Content.ReadFromJsonAsync<CachedBody>();

        // callCount stays at 1 because second call was served from cache
        Assert.Equal(1, first?.CallCount);
        Assert.Equal(1, second?.CallCount);
    }

    // ── EnvelopeResultFilter (IResultFilter) ──────────────────────────────────

    [Fact]
    public async Task Envelope_Returns200()
    {
        var response = await _client.GetAsync("/advanced-filters/envelope");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Envelope_ResponseBodyContainsDataAndMeta()
    {
        var body = await _client.GetFromJsonAsync<EnvelopeBody>("/advanced-filters/envelope");
        Assert.NotNull(body?.Data);
        Assert.NotNull(body?.Meta);
    }

    [Fact]
    public async Task Envelope_MetaContainsVersion()
    {
        var body = await _client.GetFromJsonAsync<EnvelopeBody>("/advanced-filters/envelope");
        Assert.Equal("06-C", body?.Meta?.Version);
    }

    // ── ApiKeyEndpointFilter (IEndpointFilter) ────────────────────────────────

    [Fact]
    public async Task SecureEndpoint_WithoutApiKey_Returns401()
    {
        var response = await _client.GetAsync("/minimal/secure");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SecureEndpoint_WithWrongApiKey_Returns401()
    {
        var response = await _client.GetAsync("/minimal/secure?apiKey=wrong");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SecureEndpoint_WithCorrectApiKey_Returns200()
    {
        var response = await _client.GetAsync("/minimal/secure?apiKey=lesson06");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SecureEndpoint_WithCorrectApiKey_ReturnsSecret()
    {
        var body = await _client.GetFromJsonAsync<SecretBody>("/minimal/secure?apiKey=lesson06");
        Assert.Equal("you have the key!", body?.Secret);
    }

    private record CachedBody(int CallCount, bool Cached);
    private record EnvelopeBody(System.Text.Json.JsonElement? Data, MetaBody? Meta);
    private record MetaBody(string? Version, DateTime Timestamp);
    private record SecretBody(string? Secret);
}
