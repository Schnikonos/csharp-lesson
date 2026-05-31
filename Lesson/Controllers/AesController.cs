using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;

namespace Lesson.Controllers;

/// <summary>
/// Lesson 11-B — AES symmetric encryption and IV/key management.
///
/// AES (Advanced Encryption Standard) is the gold standard for symmetric encryption.
///
/// Key concepts:
///   Key     — 128, 192, or 256 bits; kept secret; both parties must have it.
///   IV      — Initialization Vector; random per-message; NOT secret, travels with ciphertext.
///             Without a random IV, encrypting the same plaintext always produces the same
///             ciphertext — which leaks information (e.g. ECB mode vulnerability).
///   Mode    — CBC (Cipher Block Chaining) + PKCS7 padding is the classic choice.
///             GCM (Galois/Counter Mode) adds authentication; prefer it for new code.
///   Padding — PKCS7 pads plaintext to a multiple of the block size (16 bytes for AES).
///
/// Java parallel:
///   Cipher.getInstance("AES/CBC/PKCS5Padding") → Aes.Create() with CipherMode.CBC
///   KeyGenerator.generateKey()                  → Aes.Create().GenerateKey()
///
/// IMPORTANT: In production, store the key in a vault (Azure Key Vault, AWS KMS),
/// NOT hardcoded or in appsettings.json.
/// </summary>
[ApiController]
[Route("crypto/aes")]
public class AesController : ControllerBase
{
    // POST /crypto/aes/encrypt — encrypt plaintext with the provided base64 key
    [HttpPost("encrypt")]
    public IActionResult Encrypt([FromBody] AesEncryptRequest request)
    {
        byte[] key;
        try { key = Convert.FromBase64String(request.KeyBase64); }
        catch { return BadRequest(new { error = "Invalid key: must be valid Base64" }); }

        if (key.Length is not (16 or 24 or 32))
            return BadRequest(new { error = "Key must be 16, 24, or 32 bytes (128/192/256-bit)" });

        using var aes = Aes.Create();
        aes.Key  = key;
        aes.Mode = CipherMode.CBC;

        // GenerateIV() fills aes.IV with cryptographically random bytes.
        // The IV is generated PER ENCRYPTION CALL — never reuse an IV with the same key.
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor();
        var plainBytes  = Encoding.UTF8.GetBytes(request.Plaintext);
        var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        return Ok(new
        {
            // The IV is prepended to the ciphertext so the receiver can extract it.
            // Alternatively you can send them separately — but never reuse the IV.
            ciphertext = Convert.ToBase64String(cipherBytes),
            iv         = Convert.ToBase64String(aes.IV)
        });
    }

    // POST /crypto/aes/decrypt — decrypt ciphertext using key + IV
    [HttpPost("decrypt")]
    public IActionResult Decrypt([FromBody] AesDecryptRequest request)
    {
        byte[] key, iv, cipher;
        try
        {
            key    = Convert.FromBase64String(request.KeyBase64);
            iv     = Convert.FromBase64String(request.IvBase64);
            cipher = Convert.FromBase64String(request.CiphertextBase64);
        }
        catch { return BadRequest(new { error = "Invalid Base64 in key, iv, or ciphertext" }); }

        if (key.Length is not (16 or 24 or 32))
            return BadRequest(new { error = "Key must be 16, 24, or 32 bytes" });

        using var aes = Aes.Create();
        aes.Key  = key;
        aes.IV   = iv;
        aes.Mode = CipherMode.CBC;

        using var decryptor = aes.CreateDecryptor();
        var plainBytes = decryptor.TransformFinalBlock(cipher, 0, cipher.Length);

        return Ok(new { plaintext = Encoding.UTF8.GetString(plainBytes) });
    }

    // GET /crypto/aes/generate-key?bits=256 — generate a new random AES key
    [HttpGet("generate-key")]
    public IActionResult GenerateKey([FromQuery] int bits = 256)
    {
        if (bits is not (128 or 192 or 256))
            return BadRequest(new { error = "bits must be 128, 192, or 256" });

        using var aes = Aes.Create();
        aes.KeySize = bits;
        aes.GenerateKey();

        return Ok(new
        {
            bits,
            keyBase64 = Convert.ToBase64String(aes.Key)
        });
    }
}

public record AesEncryptRequest(string Plaintext, string KeyBase64);
public record AesDecryptRequest(string CiphertextBase64, string IvBase64, string KeyBase64);
