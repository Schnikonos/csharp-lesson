using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Lesson.Controllers;

/// <summary>
/// Lesson 13-A — JWT Authentication: login endpoint + protected resources.
///
/// Flow:
///   1. POST /auth/login   → validates credentials → returns JWT bearer token
///   2. GET  /auth/me      → [Authorize] → reads claims from HttpContext.User
///   3. GET  /auth/profile → [Authorize] → shows how to read specific claim values
///
/// Java parallel:
///   Spring Security AuthenticationManager + UsernamePasswordAuthenticationToken
///   → JwtAuthController manually issues tokens using JwtBuilder (JJWT library)
///
/// In production never hard-code credentials.
/// Use Identity / external IdP instead of manual password checks.
/// </summary>
[ApiController]
[Route("auth")]
public class JwtAuthController(IOptions<JwtOptions> jwtOpts) : ControllerBase
{
    // Pretend user store — in production use ASP.NET Core Identity
    private static readonly Dictionary<string, (string PasswordHash, string Role)> Users = new()
    {
        ["alice"] = (BCrypt.Net.BCrypt.HashPassword("password123"), "Teller"),
        ["bob"]   = (BCrypt.Net.BCrypt.HashPassword("admin456"),    "Manager"),
    };

    // ── POST /auth/login ──────────────────────────────────────────────────────
    [HttpPost("login")]
    [AllowAnonymous]
    public IActionResult Login([FromBody] LoginRequest request)
    {
        if (!Users.TryGetValue(request.Username, out var user))
            return Unauthorized(new { error = "Invalid credentials." });

        if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            return Unauthorized(new { error = "Invalid credentials." });

        var token = GenerateToken(request.Username, user.Role);
        return Ok(new { token, expiresIn = jwtOpts.Value.ExpirySeconds });
    }

    // ── GET /auth/me — requires a valid JWT ───────────────────────────────────
    [HttpGet("me")]
    [Authorize]
    public IActionResult Me()
    {
        // HttpContext.User is populated by the JWT middleware when the token is valid.
        // Java parallel: SecurityContextHolder.getContext().getAuthentication().getPrincipal()
        var username = User.Identity?.Name;
        var role     = User.FindFirstValue(ClaimTypes.Role);

        return Ok(new { username, role });
    }

    // ── GET /auth/profile — read individual claims ────────────────────────────
    [HttpGet("profile")]
    [Authorize]
    public IActionResult Profile()
    {
        var claims = User.Claims.Select(c => new { c.Type, c.Value });
        return Ok(new { claims });
    }

    // ── Token factory ─────────────────────────────────────────────────────────
    private string GenerateToken(string username, string role)
    {
        var opts = jwtOpts.Value;
        var key  = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(opts.SecretKey));
        var cred = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.Name,  username),
            new Claim(ClaimTypes.Role,  role),
            new Claim(JwtRegisteredClaimNames.Sub,  username),
            new Claim(JwtRegisteredClaimNames.Jti,  Guid.NewGuid().ToString()),
        };

        var token = new JwtSecurityToken(
            issuer:   opts.Issuer,
            audience: opts.Audience,
            claims:   claims,
            expires:  DateTime.UtcNow.AddSeconds(opts.ExpirySeconds),
            signingCredentials: cred);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

public record LoginRequest(string Username, string Password);

/// <summary>JWT configuration — bound from appsettings.json "Jwt" section.</summary>
public class JwtOptions
{
    public string SecretKey    { get; init; } = "lesson-super-secret-key-32-bytes!";
    public string Issuer       { get; init; } = "LessonBankingApp";
    public string Audience     { get; init; } = "LessonBankingApp";
    public int    ExpirySeconds { get; init; } = 3600;
}
