using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;

namespace Lesson.Tests;

/// <summary>
/// Lesson 13-C — Refresh token and revocation integration tests.
/// </summary>
public class RefreshTokenTests : IClassFixture<AccountsTestFactory>
{
    private readonly AccountsTestFactory _factory;
    public RefreshTokenTests(AccountsTestFactory factory) => _factory = factory;

    // ── Helpers ───────────────────────────────────────────────────────────────

    private HttpClient NewClient() => _factory.CreateClient(
        new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
            { AllowAutoRedirect = false });

    private async Task<TokenPairResponse> LoginAsync(string user, string pass)
    {
        var client   = NewClient();
        var response = await client.PostAsJsonAsync("/auth/token/login",
            new { username = user, password = pass });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TokenPairResponse>())!;
    }

    // ── Login returns both tokens ─────────────────────────────────────────────

    [Fact]
    public async Task Login_Returns_AccessToken_And_RefreshToken()
    {
        var pair = await LoginAsync("alice", "password123");
        pair.AccessToken.Should().NotBeNullOrWhiteSpace();
        pair.RefreshToken.Should().NotBeNullOrWhiteSpace();
        pair.ExpiresIn.Should().Be(3600);
    }

    // ── Refresh flow ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Refresh_ValidToken_ReturnsNewTokenPair()
    {
        var original = await LoginAsync("alice", "password123");
        var client   = NewClient();

        var resp = await client.PostAsJsonAsync("/auth/token/refresh",
            new { refreshToken = original.RefreshToken });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var newPair = await resp.Content.ReadFromJsonAsync<TokenPairResponse>();
        newPair!.AccessToken.Should().NotBeNullOrWhiteSpace();
        // new refresh token is different (single-use)
        newPair.RefreshToken.Should().NotBe(original.RefreshToken);
    }

    [Fact]
    public async Task Refresh_UsedToken_Returns401()
    {
        var pair   = await LoginAsync("alice", "password123");
        var client = NewClient();

        // Use the refresh token once
        await client.PostAsJsonAsync("/auth/token/refresh",
            new { refreshToken = pair.RefreshToken });

        // Second use must fail
        var resp = await client.PostAsJsonAsync("/auth/token/refresh",
            new { refreshToken = pair.RefreshToken });
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_InvalidToken_Returns401()
    {
        var client = NewClient();
        var resp   = await client.PostAsJsonAsync("/auth/token/refresh",
            new { refreshToken = "this-is-not-valid" });
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Revocation ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Revoke_ThenRefresh_Returns401()
    {
        var pair = await LoginAsync("bob", "admin456");

        // Revoke the refresh token using the access token
        var client = NewClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", pair.AccessToken);

        var revokeResp = await client.PostAsJsonAsync("/auth/token/revoke",
            new { refreshToken = pair.RefreshToken });
        revokeResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Now try to refresh — must fail
        var refreshResp = await NewClient().PostAsJsonAsync("/auth/token/refresh",
            new { refreshToken = pair.RefreshToken });
        refreshResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Revoke_WithoutToken_Returns401()
    {
        var resp = await NewClient().PostAsJsonAsync("/auth/token/revoke",
            new { refreshToken = "any" });
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}

internal record TokenPairResponse(string AccessToken, string RefreshToken, int ExpiresIn);
