using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Lesson.Data;
using FluentAssertions;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Lesson.Tests;

// =============================================================================
// LESSON 26-C Tests: Security Hardening
//
// Validates:
//  • CSP / X-Frame-Options / X-Content-Type-Options / Referrer-Policy headers
//  • HttpOnly + Secure + SameSite=Strict flags on the session cookie
//  • Anti-forgery: POST without token → 400
//  • Anti-forgery: POST with token from GET /api/login/token → 200
//  • XSS prevention: Razor auto-encodes dangerous output
// =============================================================================
public class SecurityHeadersTests : IClassFixture<SecurityTestFactory>
{
    private readonly HttpClient _client;

    public SecurityHeadersTests(SecurityTestFactory factory)
    {
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    // -------------------------------------------------------------------------
    // Security response headers
    // -------------------------------------------------------------------------
    [Fact]
    public async Task Response_ContainsContentSecurityPolicyHeader()
    {
        var response = await _client.GetAsync("/");

        response.Headers.Should().ContainKey("Content-Security-Policy");
        var csp = response.Headers.GetValues("Content-Security-Policy").First();
        csp.Should().Contain("default-src 'self'");
    }

    [Fact]
    public async Task Response_ContainsXFrameOptionsHeader()
    {
        var response = await _client.GetAsync("/");

        response.Headers.Should().ContainKey("X-Frame-Options");
        response.Headers.GetValues("X-Frame-Options").Should().Contain("DENY");
    }

    [Fact]
    public async Task Response_ContainsXContentTypeOptionsHeader()
    {
        var response = await _client.GetAsync("/");

        response.Headers.Should().ContainKey("X-Content-Type-Options");
        response.Headers.GetValues("X-Content-Type-Options").Should().Contain("nosniff");
    }

    [Fact]
    public async Task Response_ContainsReferrerPolicyHeader()
    {
        var response = await _client.GetAsync("/");

        response.Headers.Should().ContainKey("Referrer-Policy");
        response.Headers.GetValues("Referrer-Policy")
            .Should().Contain("strict-origin-when-cross-origin");
    }

    // -------------------------------------------------------------------------
    // Cookie security flags
    // -------------------------------------------------------------------------
    [Fact]
    public async Task LoginPost_SessionCookie_HasSecurityFlags()
    {
        // Arrange: get an anti-forgery token first
        var tokenResponse = await _client.GetAsync("/api/login/token");
        tokenResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var tokenJson = await tokenResponse.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(tokenJson);
        var token = doc.RootElement.GetProperty("token").GetString()!;
        var headerName = doc.RootElement.GetProperty("headerName").GetString()!;

        // Act: POST with anti-forgery header
        var body = JsonSerializer.Serialize(new { username = "alice", password = "secret" });
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/login")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Add(headerName, token);
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // The "session" cookie must have HttpOnly + SameSite=Strict
        var cookies = response.Headers.TryGetValues("Set-Cookie", out var cookieValues)
            ? cookieValues.ToList()
            : [];

        var sessionCookie = cookies.FirstOrDefault(c => c.StartsWith("session="));
        sessionCookie.Should().NotBeNull("a session cookie should be set");
        sessionCookie!.ToLowerInvariant().Should().Contain("httponly");
        sessionCookie.ToLowerInvariant().Should().Contain("samesite=strict");
    }

    // -------------------------------------------------------------------------
    // Anti-forgery
    // -------------------------------------------------------------------------
    [Fact]
    public async Task PostStatement_WithoutAntiforgeryToken_Returns400()
    {
        // Razor Pages auto-validate anti-forgery on POST
        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("accountNumber", "ACC-001")
        });
        var response = await _client.PostAsync("/Statement", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // -------------------------------------------------------------------------
    // XSS prevention via Razor auto-encoding
    // -------------------------------------------------------------------------
    [Fact]
    public async Task StatementPage_XssPayload_IsHtmlEncoded()
    {
        // Simulate a malicious accountNumber that contains a <script> tag.
        // Razor's @Model.AccountNumber auto-encodes it — the raw tag must NOT appear.
        var response = await _client.GetAsync("/Statement?accountNumber=<script>alert(1)</script>");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();

        // The raw script tag must NOT be present
        html.Should().NotContain("<script>alert(1)</script>");
        // The HTML-encoded version IS present (proving encoding happened)
        html.Should().Contain("&lt;script&gt;");
    }
}

// =============================================================================
// Rate limiting tests — own fixture so the window counter is independent
// =============================================================================
public class RateLimitingTests : IClassFixture<RateLimitTestFactory>
{
    private readonly HttpClient _client;

    public RateLimitingTests(RateLimitTestFactory factory)
    {
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    [Fact]
    public async Task LoginPost_ExceedingRateLimit_Returns429()
    {
        // The rate limit is 5 per minute per IP. Fire 6 requests; the 6th must be 429.
        HttpResponseMessage? lastResponse = null;
        for (int i = 0; i < 6; i++)
        {
            var body = JsonSerializer.Serialize(new { username = $"user{i}", password = "pw" });
            lastResponse = await _client.PostAsync("/api/login",
                new StringContent(body, Encoding.UTF8, "application/json"));
        }

        lastResponse!.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }
}

// ---------------------------------------------------------------------------
// Test factories
// ---------------------------------------------------------------------------
public class SecurityTestFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection;

    public SecurityTestFactory()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
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

public class RateLimitTestFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection;

    public RateLimitTestFactory()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            var d = services.SingleOrDefault(
                s => s.ServiceType == typeof(DbContextOptions<BankingDbContext>));
            if (d is not null) services.Remove(d);
            services.AddDbContext<BankingDbContext>(o => o.UseSqlite(_connection));
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
