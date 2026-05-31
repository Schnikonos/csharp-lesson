using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Lesson.Controllers;

// =============================================================================
// LESSON 26-C: LoginController — rate-limited authentication endpoint
//
// Demonstrates:
//   • [EnableRateLimiting] attribute on a controller action
//   • Fixed-window rate limiter (5 attempts per minute per IP)
//   • HttpOnly / Secure / SameSite cookie flags on the session cookie
//   • IAntiforgery usage for non-Razor-Page endpoints
//
// Java parallel:
//   Spring Security: http.sessionManagement() + @RateLimiter (Resilience4j)
//   or bucket4j-spring-boot-starter
// =============================================================================
[ApiController]
[Route("api/[controller]")]
public class LoginController : ControllerBase
{
    private readonly IAntiforgery _antiforgery;
    private readonly ILogger<LoginController> _logger;

    public LoginController(IAntiforgery antiforgery, ILogger<LoginController> logger)
    {
        _antiforgery = antiforgery;
        _logger = logger;
    }

    // -------------------------------------------------------------------------
    // GET /api/login/token
    // Returns an anti-forgery token for the client to include in POST requests.
    // Java parallel: Spring Security's CsrfTokenRequestHandler / CsrfToken endpoint
    // -------------------------------------------------------------------------
    [HttpGet("token")]
    public IActionResult GetToken()
    {
        var tokens = _antiforgery.GetAndStoreTokens(HttpContext);
        return Ok(new { token = tokens.RequestToken, headerName = tokens.HeaderName });
    }

    // -------------------------------------------------------------------------
    // POST /api/login
    // Rate-limited to 5 requests per minute per IP (fixed-window policy "login").
    // Sets a session cookie with HttpOnly + Secure + SameSite=Strict flags.
    //
    // In a real app this would validate credentials and issue a JWT or session.
    // -------------------------------------------------------------------------
    [HttpPost]
    [EnableRateLimiting("login")]
    public IActionResult Login([FromBody] LoginRequest request)
    {
        _logger.LogInformation("Login attempt for {Username}", request.Username);

        // Simulate authentication (always succeeds in this lesson)
        if (string.IsNullOrWhiteSpace(request.Username))
            return BadRequest(new { error = "Username is required" });

        // Set a session cookie with security flags.
        // Java parallel: response.addCookie(new Cookie("session", value)
        //                  .setHttpOnly(true).setSecure(true).setSameSite("Strict"))
        Response.Cookies.Append("session", Guid.NewGuid().ToString(), new CookieOptions
        {
            HttpOnly = true,                    // not accessible from JavaScript
            Secure   = true,                    // HTTPS only
            SameSite = SameSiteMode.Strict,     // no cross-site sending
            Expires  = DateTimeOffset.UtcNow.AddHours(1)
        });

        return Ok(new { message = $"Welcome, {request.Username}!" });
    }
}

public record LoginRequest(string Username, string Password);
