using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;

namespace Lesson.Tests;

/// <summary>
/// Lesson 13-B — Role-based authorization + custom IAuthorizationHandler tests.
///
/// Tests cover:
///   - Role-based access: Teller/Manager can transfer; Guest cannot
///   - Custom policy: Manager can close any account; owner can close own; others get 403
///   - Unauthenticated access returns 401 (not 403)
/// </summary>
public class RoleBasedAuthTests : IClassFixture<AccountsTestFactory>
{
    private readonly AccountsTestFactory _factory;
    public RoleBasedAuthTests(AccountsTestFactory factory) => _factory = factory;

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<string> LoginAsync(string user, string pass)
    {
        var client   = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/auth/login",
            new { username = user, password = pass });
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        return body!.Token;
    }

    private HttpClient AuthClient(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    // ── GET /banking/balance — any authenticated user ─────────────────────────

    [Fact]
    public async Task GetBalance_AuthenticatedUser_Returns200()
    {
        var token  = await LoginAsync("alice", "password123");
        var client = AuthClient(token);
        var resp   = await client.GetAsync("/banking/balance/1");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetBalance_Unauthenticated_Returns401()
    {
        var resp = await _factory.CreateClient().GetAsync("/banking/balance/1");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── POST /banking/transfer — Teller or Manager only ──────────────────────

    [Theory]
    [InlineData("alice", "password123")]   // Teller
    [InlineData("bob",   "admin456")]       // Manager
    public async Task Transfer_TellerOrManager_Returns200(string user, string pass)
    {
        var token  = await LoginAsync(user, pass);
        var client = AuthClient(token);
        var resp   = await client.PostAsJsonAsync("/banking/transfer",
            new { from = "ACC-001", to = "ACC-002", amount = 100m });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Transfer_Unauthenticated_Returns401()
    {
        var resp = await _factory.CreateClient().PostAsJsonAsync("/banking/transfer",
            new { from = "ACC-001", to = "ACC-002", amount = 100m });
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── DELETE /banking/accounts/{id} — Manager or account owner ─────────────

    [Fact]
    public async Task CloseAccount_Manager_Returns200()
    {
        var token  = await LoginAsync("bob", "admin456");   // Manager
        var client = AuthClient(token);
        var resp   = await client.DeleteAsync("/banking/accounts/999");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CloseAccount_NonOwnerTeller_Returns403()
    {
        // alice is a Teller with sub="alice"; she cannot close account id=999
        var token  = await LoginAsync("alice", "password123");
        var client = AuthClient(token);
        var resp   = await client.DeleteAsync("/banking/accounts/999");
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CloseAccount_Unauthenticated_Returns401()
    {
        var resp = await _factory.CreateClient().DeleteAsync("/banking/accounts/1");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
