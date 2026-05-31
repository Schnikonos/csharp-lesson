using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Lesson.Tests;

/// <summary>
/// Lesson 13-A — JWT Authentication integration tests.
///
/// Tests exercise the full HTTP pipeline:
///   POST /auth/login  → credentials → JWT token
///   GET  /auth/me     → [Authorize] → claims
///   GET  /auth/profile → [Authorize] → all claims list
///
/// Java parallel:
///   @SpringBootTest + MockMvc + SecurityMockMvcRequestPostProcessors.jwt()
///   → WebApplicationFactory + HttpClient + Authorization: Bearer <token>
/// </summary>
public class JwtAuthTests : IClassFixture<AccountsTestFactory>
{
    private readonly HttpClient _client;

    public JwtAuthTests(AccountsTestFactory factory)
        => _client = factory.CreateClient(
               new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    // ── Login ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task POST_Login_ValidCredentials_Returns200_WithToken()
    {
        var response = await _client.PostAsJsonAsync("/auth/login",
            new { username = "alice", password = "password123" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        body!.Token.Should().NotBeNullOrWhiteSpace();
        body.ExpiresIn.Should().Be(3600);
    }

    [Theory]
    [InlineData("alice",   "wrongpassword")]
    [InlineData("nobody",  "password123")]
    [InlineData("",        "")]
    public async Task POST_Login_InvalidCredentials_Returns401(string user, string pass)
    {
        var response = await _client.PostAsJsonAsync("/auth/login",
            new { username = user, password = pass });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Protected endpoints — unauthenticated ─────────────────────────────────

    [Theory]
    [InlineData("/auth/me")]
    [InlineData("/auth/profile")]
    public async Task GET_Protected_WithoutToken_Returns401(string url)
    {
        var response = await _client.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Protected endpoints — authenticated ───────────────────────────────────

    [Fact]
    public async Task GET_Me_WithValidToken_Returns200_WithUsernameAndRole()
    {
        var token    = await LoginAndGetTokenAsync("alice", "password123");
        var response = await GetAuthenticatedAsync("/auth/me", token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<MeResponse>();
        body!.Username.Should().Be("alice");
        body.Role.Should().Be("Teller");
    }

    [Fact]
    public async Task GET_Me_DifferentUser_ReturnsCorrectRole()
    {
        var token    = await LoginAndGetTokenAsync("bob", "admin456");
        var response = await GetAuthenticatedAsync("/auth/me", token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<MeResponse>();
        body!.Username.Should().Be("bob");
        body.Role.Should().Be("Manager");
    }

    [Fact]
    public async Task GET_Profile_WithValidToken_ContainsExpectedClaims()
    {
        var token    = await LoginAndGetTokenAsync("alice", "password123");
        var response = await GetAuthenticatedAsync("/auth/profile", token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ProfileResponse>();
        body!.Claims.Should().NotBeEmpty();
        body.Claims.Should().Contain(c => c.Value == "alice");
    }

    [Fact]
    public async Task GET_Me_WithTamperedToken_Returns401()
    {
        var token   = await LoginAndGetTokenAsync("alice", "password123");
        var tampered = token[..^4] + "XXXX";     // corrupt the signature

        var response = await GetAuthenticatedAsync("/auth/me", tampered);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<string> LoginAndGetTokenAsync(string user, string pass)
    {
        var response = await _client.PostAsJsonAsync("/auth/login",
            new { username = user, password = pass });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        return body!.Token;
    }

    private Task<HttpResponseMessage> GetAuthenticatedAsync(string url, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return _client.SendAsync(request);
    }
}

internal record LoginResponse(string Token, int ExpiresIn);
internal record MeResponse(string Username, string Role);
internal record ClaimEntry(string Type, string Value);
internal record ProfileResponse(IReadOnlyList<ClaimEntry> Claims);
