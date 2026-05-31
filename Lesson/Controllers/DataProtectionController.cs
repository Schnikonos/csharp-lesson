using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;

namespace Lesson.Controllers;

/// <summary>
/// Lesson 11-C — ASP.NET Core Data Protection API.
///
/// IDataProtectionProvider is the .NET equivalent of Jasypt's StandardPBEStringEncryptor:
///   - Keys are auto-generated and stored on disk (or Azure Key Vault in production).
///   - Key rotation is built in — older keys are retained to decrypt existing data.
///   - Payloads are authenticated (HMAC) in addition to being encrypted — tampering detected.
///   - "Purpose" strings create isolated key rings; data protected with purpose A
///     cannot be decrypted with purpose B, even with the same key ring on disk.
///
/// Java parallel:
///   Jasypt StandardPBEStringEncryptor → IDataProtector (Protect/Unprotect)
///   Jasypt setPassword(...)            → handled automatically by the key ring
///
/// ITimeLimitedDataProtector adds an expiry — perfect for one-time tokens, password-reset
/// links, email verification codes, etc.
/// </summary>
[ApiController]
[Route("crypto/dp")]
public class DataProtectionController(IDataProtectionProvider dpProvider) : ControllerBase
{
    // Each unique "purpose" creates an isolated sub-key.
    // A payload protected with one purpose cannot be unprotected with another.
    private IDataProtector Protector(string purpose) =>
        dpProvider.CreateProtector(purpose);

    // ── Protect / Unprotect ───────────────────────────────────────────────────

    // POST /crypto/dp/protect — encrypt + authenticate a string value
    [HttpPost("protect")]
    public IActionResult Protect([FromBody] DpProtectRequest request)
    {
        var purpose   = request.Purpose ?? "default";
        var protector = Protector(purpose);
        var token     = protector.Protect(request.Plaintext);
        return Ok(new { token, purpose });
    }

    // POST /crypto/dp/unprotect — decrypt and verify authenticity
    [HttpPost("unprotect")]
    public IActionResult Unprotect([FromBody] DpUnprotectRequest request)
    {
        var purpose   = request.Purpose ?? "default";
        var protector = Protector(purpose);

        try
        {
            var plaintext = protector.Unprotect(request.Token);
            return Ok(new { plaintext });
        }
        catch (System.Security.Cryptography.CryptographicException ex)
        {
            // Wrong purpose, tampered token, or expired token
            return BadRequest(new { error = ex.Message });
        }
    }

    // ── Time-limited tokens ───────────────────────────────────────────────────

    // POST /crypto/dp/token/create — protect with a TTL (e.g. password-reset link)
    // Java parallel: no direct Jasypt equivalent — typically hand-rolled with JWT exp claim
    [HttpPost("token/create")]
    public IActionResult CreateToken([FromBody] DpTokenCreateRequest request)
    {
        var timeLimited = dpProvider
            .CreateProtector("PasswordReset")
            .ToTimeLimitedDataProtector();

        var expiry = DateTimeOffset.UtcNow.AddSeconds(request.TtlSeconds);
        var token  = timeLimited.Protect(request.Payload, expiry);

        return Ok(new { token, expiresAt = expiry });
    }

    // POST /crypto/dp/token/consume — unprotect; fails if expired
    [HttpPost("token/consume")]
    public IActionResult ConsumeToken([FromBody] DpTokenConsumeRequest request)
    {
        var timeLimited = dpProvider
            .CreateProtector("PasswordReset")
            .ToTimeLimitedDataProtector();

        try
        {
            var payload = timeLimited.Unprotect(request.Token, out var expiry);
            return Ok(new { payload, expiresAt = expiry });
        }
        catch (System.Security.Cryptography.CryptographicException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}

public record DpProtectRequest(string Plaintext, string? Purpose);
public record DpUnprotectRequest(string Token, string? Purpose);
public record DpTokenCreateRequest(string Payload, int TtlSeconds);
public record DpTokenConsumeRequest(string Token);
