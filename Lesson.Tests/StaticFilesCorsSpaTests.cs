using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Lesson.Data;
using FluentAssertions;
using System.Net;

namespace Lesson.Tests;

// =============================================================================
// LESSON 26-A Tests: Static Files, CORS, SPA Fallback, HTTPS redirect
//
// Demonstrates testing:
//  • wwwroot static file serving (index.html, CSS, JS)
//  • UseDefaultFiles: GET / → index.html
//  • SPA fallback: unknown routes → index.html (not 404)
//  • CORS preflight: OPTIONS with Origin returns correct headers
//  • CORS simple request: GET with Origin returns Access-Control-Allow-Origin
// =============================================================================
public class StaticFilesCorsSpaTests : IClassFixture<FrontendTestFactory>
{
    private readonly HttpClient _client;

    public StaticFilesCorsSpaTests(FrontendTestFactory factory)
    {
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            // Don't follow redirects so we can assert 301 HTTP→HTTPS in isolation
            AllowAutoRedirect = false
        });
    }

    // -------------------------------------------------------------------------
    // Static files
    // -------------------------------------------------------------------------
    [Fact]
    public async Task GetIndex_ReturnsHtml()
    {
        var response = await _client.GetAsync("/index.html");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Sandbox Bank");
    }

    [Fact]
    public async Task GetCss_ReturnsTextCss()
    {
        var response = await _client.GetAsync("/css/app.css");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/css");
    }

    [Fact]
    public async Task GetJs_ReturnsJavascript()
    {
        var response = await _client.GetAsync("/js/app.js");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        // application/javascript or text/javascript — both are acceptable
        response.Content.Headers.ContentType!.MediaType.Should()
            .BeOneOf("application/javascript", "text/javascript");
    }

    // -------------------------------------------------------------------------
    // UseDefaultFiles: GET / → serves index.html content
    // -------------------------------------------------------------------------
    [Fact]
    public async Task GetRoot_ServesIndexHtml()
    {
        var response = await _client.GetAsync("/");

        // 200 + HTML body (UseDefaultFiles rewrites "/" → "/index.html" internally)
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("<!DOCTYPE html");
    }

    // -------------------------------------------------------------------------
    // SPA fallback: unknown path → index.html (not 404)
    // -------------------------------------------------------------------------
    [Fact]
    public async Task GetUnknownRoute_FallsBackToIndexHtml()
    {
        var response = await _client.GetAsync("/accounts/dashboard");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("<!DOCTYPE html");
    }

    // -------------------------------------------------------------------------
    // CORS — simple request (GET with allowed Origin)
    // -------------------------------------------------------------------------
    [Fact]
    public async Task GetApiWithAllowedOrigin_ReturnsAccessControlHeader()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/templating/email/TXN-001");
        request.Headers.Add("Origin", "http://localhost:3000");

        var response = await _client.SendAsync(request);

        response.Headers.Should().ContainKey("Access-Control-Allow-Origin");
        response.Headers.GetValues("Access-Control-Allow-Origin")
            .Should().Contain("http://localhost:3000");
    }

    [Fact]
    public async Task GetApiWithDisallowedOrigin_DoesNotReturnAccessControlHeader()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/templating/email/TXN-001");
        request.Headers.Add("Origin", "http://evil.example.com");

        var response = await _client.SendAsync(request);

        response.Headers.Should().NotContainKey("Access-Control-Allow-Origin");
    }

    // -------------------------------------------------------------------------
    // CORS — preflight (OPTIONS)
    // -------------------------------------------------------------------------
    [Fact]
    public async Task PreflightRequest_AllowedOrigin_Returns204WithCorsHeaders()
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/templating/email/TXN-001");
        request.Headers.Add("Origin", "http://localhost:3000");
        request.Headers.Add("Access-Control-Request-Method", "GET");
        request.Headers.Add("Access-Control-Request-Headers", "Content-Type");

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().BeOneOf(HttpStatusCode.NoContent, HttpStatusCode.OK);
        response.Headers.Should().ContainKey("Access-Control-Allow-Origin");
        response.Headers.Should().ContainKey("Access-Control-Allow-Methods");
    }
}

// ---------------------------------------------------------------------------
// Test factory — uses in-memory SQLite so no real DB is needed
// ---------------------------------------------------------------------------
public class FrontendTestFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection;

    public FrontendTestFactory()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.UseSetting("ASPNETCORE_ENVIRONMENT", "Development");
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
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _connection.Dispose();
    }
}
