using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;

namespace Lesson.Controllers;

/// <summary>
/// Lesson 11-A — Base64, SHA-256 hashing, HMACSHA256, BCrypt password hashing.
///
/// Java parallels:
///   Base64.getEncoder().encodeToString(bytes)  →  Convert.ToBase64String(bytes)
///   MessageDigest.getInstance("SHA-256")        →  SHA256.HashData(bytes)
///   Mac.getInstance("HmacSHA256")               →  HMACSHA256
///   BCryptPasswordEncoder (Spring Security)     →  BCrypt.Net.BCrypt.HashPassword
/// </summary>
[ApiController]
[Route("crypto")]
public class CryptoBasicController : ControllerBase
{
    // ── Base64 ────────────────────────────────────────────────────────────────

    // POST /crypto/base64/encode — encode plain text to Base64
    [HttpPost("base64/encode")]
    public IActionResult Encode([FromBody] TextRequest request)
    {
        var bytes = Encoding.UTF8.GetBytes(request.Text);
        var encoded = Convert.ToBase64String(bytes);
        return Ok(new { encoded });
    }

    // POST /crypto/base64/decode — decode Base64 back to plain text
    [HttpPost("base64/decode")]
    public IActionResult Decode([FromBody] TextRequest request)
    {
        try
        {
            var bytes = Convert.FromBase64String(request.Text);
            var decoded = Encoding.UTF8.GetString(bytes);
            return Ok(new { decoded });
        }
        catch (FormatException)
        {
            return BadRequest(new { error = "Invalid Base64 input" });
        }
    }

    // ── SHA-256 hashing ───────────────────────────────────────────────────────

    // POST /crypto/sha256 — one-way hash of a value (e.g. document fingerprint)
    // SHA-256 is deterministic and fast — NOT suitable for passwords.
    // Java: MessageDigest.getInstance("SHA-256").digest(bytes)
    [HttpPost("sha256")]
    public IActionResult Sha256Hash([FromBody] TextRequest request)
    {
        var bytes = Encoding.UTF8.GetBytes(request.Text);

        // SHA256.HashData — static, allocation-efficient (no need to instantiate)
        var hash = SHA256.HashData(bytes);
        var hex  = Convert.ToHexString(hash).ToLowerInvariant();

        return Ok(new { hex, base64 = Convert.ToBase64String(hash) });
    }

    // ── HMACSHA256 ────────────────────────────────────────────────────────────

    // POST /crypto/hmac — keyed hash; used to verify message integrity + authenticity
    // Java: Mac.getInstance("HmacSHA256"); mac.init(new SecretKeySpec(key, "HmacSHA256"))
    [HttpPost("hmac")]
    public IActionResult HmacHash([FromBody] HmacRequest request)
    {
        var keyBytes  = Encoding.UTF8.GetBytes(request.Key);
        var dataBytes = Encoding.UTF8.GetBytes(request.Data);

        using var hmac = new HMACSHA256(keyBytes);
        var hash = hmac.ComputeHash(dataBytes);

        return Ok(new
        {
            hex    = Convert.ToHexString(hash).ToLowerInvariant(),
            base64 = Convert.ToBase64String(hash)
        });
    }

    // POST /crypto/hmac/verify — constant-time comparison to prevent timing attacks
    [HttpPost("hmac/verify")]
    public IActionResult HmacVerify([FromBody] HmacVerifyRequest request)
    {
        var keyBytes  = Encoding.UTF8.GetBytes(request.Key);
        var dataBytes = Encoding.UTF8.GetBytes(request.Data);

        using var hmac  = new HMACSHA256(keyBytes);
        var expected    = hmac.ComputeHash(dataBytes);
        var actual      = Convert.FromHexString(request.ExpectedHex);

        // CryptographicOperations.FixedTimeEquals — constant-time to prevent timing side-channels
        var valid = CryptographicOperations.FixedTimeEquals(expected, actual);
        return Ok(new { valid });
    }

    // ── BCrypt password hashing ───────────────────────────────────────────────

    // POST /crypto/password/hash — bcrypt with work factor (default 11)
    // BCrypt is slow by design; the work factor makes brute-force expensive.
    // NEVER use SHA-256 for passwords — use bcrypt/argon2/scrypt instead.
    [HttpPost("password/hash")]
    public IActionResult HashPassword([FromBody] PasswordRequest request)
    {
        // BCrypt.Net.BCrypt.HashPassword generates a random salt internally
        var hash = BCrypt.Net.BCrypt.HashPassword(request.Password, workFactor: 11);
        return Ok(new { hash });
    }

    // POST /crypto/password/verify — verify a plain-text password against a bcrypt hash
    [HttpPost("password/verify")]
    public IActionResult VerifyPassword([FromBody] PasswordVerifyRequest request)
    {
        var valid = BCrypt.Net.BCrypt.Verify(request.Password, request.Hash);
        return Ok(new { valid });
    }
}

public record TextRequest(string Text);
public record HmacRequest(string Key, string Data);
public record HmacVerifyRequest(string Key, string Data, string ExpectedHex);
public record PasswordRequest(string Password);
public record PasswordVerifyRequest(string Password, string Hash);
