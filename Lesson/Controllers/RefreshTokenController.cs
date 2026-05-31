using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Lesson.Controllers;

/// <summary>
/// Lesson 13-C — Refresh tokens + token revocation.
///
/// Flow:
///   POST /auth/token/refresh    → validates refresh token → issues new access + refresh tokens
///   POST /auth/token/revoke     → invalidates refresh token (server-side deny-list)
///
/// The refresh token is an opaque random string stored in an in-memory dictionary.
/// In production: store refresh tokens in a database with UserId, expiry, and revoked flag.
///
/// Java parallel:
///   Spring Security OAuth2 Authorization Server handles this automatically.
///   Here we implement it manually to show how it works internally.
/// </summary>
[ApiController]
[Route("auth/token")]
public class RefreshTokenController(
    IOptions<JwtOptions>    jwtOpts,
    TokenStore              store) : ControllerBase
{
    // ── POST /auth/token/refresh ──────────────────────────────────────────────
    [HttpPost("refresh")]
    [AllowAnonymous]
    public IActionResult Refresh([FromBody] RefreshRequest request)
    {
        if (!store.TryConsume(request.RefreshToken, out var username, out var role))
            return Unauthorized(new { error = "Invalid or expired refresh token." });

        var (accessToken, refreshToken) = IssueTokenPair(username!, role!);
        return Ok(new TokenPairResponse(accessToken, refreshToken, jwtOpts.Value.ExpirySeconds));
    }

    // ── POST /auth/token/revoke ───────────────────────────────────────────────
    [HttpPost("revoke")]
    [Authorize]
    public IActionResult Revoke([FromBody] RevokeRequest request)
    {
        store.Revoke(request.RefreshToken);
        return NoContent();
    }

    // ── POST /auth/token/login-with-refresh ───────────────────────────────────
    // Extends the 13-A login endpoint to also return a refresh token
    [HttpPost("login")]
    [AllowAnonymous]
    public IActionResult Login([FromBody] LoginRequest request)
    {
        // Reuse the same in-memory user dict via Users lookup (no BCrypt here for brevity)
        if (!FullUsers.TryGetValue(request.Username, out var user))
            return Unauthorized(new { error = "Invalid credentials." });

        if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            return Unauthorized(new { error = "Invalid credentials." });

        var (accessToken, refreshToken) = IssueTokenPair(request.Username, user.Role);
        return Ok(new TokenPairResponse(accessToken, refreshToken, jwtOpts.Value.ExpirySeconds));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private (string AccessToken, string RefreshToken) IssueTokenPair(string username, string role)
    {
        var opts = jwtOpts.Value;
        var key  = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(opts.SecretKey));
        var cred = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.Name, username),
            new Claim(ClaimTypes.Role, role),
            new Claim(JwtRegisteredClaimNames.Sub, username),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        var accessToken = new JwtSecurityToken(
            issuer:   opts.Issuer,
            audience: opts.Audience,
            claims:   claims,
            expires:  DateTime.UtcNow.AddSeconds(opts.ExpirySeconds),
            signingCredentials: cred);

        var refreshToken = GenerateRefreshToken();
        store.Store(refreshToken, username, role, TimeSpan.FromDays(7));

        return (new JwtSecurityTokenHandler().WriteToken(accessToken), refreshToken);
    }

    private static string GenerateRefreshToken()
    {
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes);
    }

    // Pretend user store (same BCrypt hashes as 13-A)
    private static readonly Dictionary<string, (string PasswordHash, string Role)> FullUsers = new()
    {
        ["alice"] = (BCrypt.Net.BCrypt.HashPassword("password123"), "Teller"),
        ["bob"]   = (BCrypt.Net.BCrypt.HashPassword("admin456"),    "Manager"),
    };
}

// ── DTOs ──────────────────────────────────────────────────────────────────────
public record RefreshRequest(string RefreshToken);
public record RevokeRequest(string RefreshToken);
public record TokenPairResponse(string AccessToken, string RefreshToken, int ExpiresIn);

// ── Token Store (in-memory, register as Singleton) ───────────────────────────
/// <summary>
/// Simple in-memory store for refresh tokens.
/// Production equivalent: a DB table with columns (Token, UserId, Role, ExpiresAt, Revoked).
/// </summary>
public sealed class TokenStore
{
    private readonly Dictionary<string, (string Username, string Role, DateTime ExpiresAt)> _tokens = new();

    public void Store(string token, string username, string role, TimeSpan lifetime)
    {
        lock (_tokens)
            _tokens[token] = (username, role, DateTime.UtcNow.Add(lifetime));
    }

    public bool TryConsume(string token, out string? username, out string? role)
    {
        lock (_tokens)
        {
            if (!_tokens.TryGetValue(token, out var entry) || entry.ExpiresAt < DateTime.UtcNow)
            {
                username = role = null;
                return false;
            }
            _tokens.Remove(token);   // single-use: consumed on refresh
            username = entry.Username;
            role     = entry.Role;
            return true;
        }
    }

    public void Revoke(string token) { lock (_tokens) _tokens.Remove(token); }
}
