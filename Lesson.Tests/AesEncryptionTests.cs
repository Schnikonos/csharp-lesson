using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Lesson.Data;

namespace Lesson.Tests;

/// <summary>
/// Lesson 11-B integration tests — AES symmetric encryption.
/// </summary>
public class AesEncryptionTests : IClassFixture<AesTestFactory>
{
    private readonly HttpClient _client;
    public AesEncryptionTests(AesTestFactory factory) => _client = factory.CreateClient();

    // ── Key generation ────────────────────────────────────────────────────────

    [Theory]
    [InlineData(128)]
    [InlineData(192)]
    [InlineData(256)]
    public async Task GenerateKey_ReturnsBase64KeyOfCorrectSize(int bits)
    {
        var response = await _client.GetAsync($"/crypto/aes/generate-key?bits={bits}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<KeyResult>();
        Assert.Equal(bits, body!.bits);
        var keyBytes = Convert.FromBase64String(body.keyBase64);
        Assert.Equal(bits / 8, keyBytes.Length);
    }

    [Fact]
    public async Task GenerateKey_InvalidBits_Returns400()
    {
        var response = await _client.GetAsync("/crypto/aes/generate-key?bits=64");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── Encrypt ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Encrypt_Returns200_WithCiphertextAndIv()
    {
        var key = await GenerateKeyAsync(256);
        var response = await _client.PostAsJsonAsync("/crypto/aes/encrypt",
            new { plaintext = "Secret banking data", keyBase64 = key });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<EncryptResult>();
        Assert.False(string.IsNullOrEmpty(body!.ciphertext));
        Assert.False(string.IsNullOrEmpty(body.iv));
    }

    [Fact]
    public async Task Encrypt_SamePlaintextTwice_ProducesDifferentCiphertexts()
    {
        var key = await GenerateKeyAsync(256);
        var r1 = await _client.PostAsJsonAsync("/crypto/aes/encrypt",
            new { plaintext = "same", keyBase64 = key });
        var r2 = await _client.PostAsJsonAsync("/crypto/aes/encrypt",
            new { plaintext = "same", keyBase64 = key });

        var c1 = (await r1.Content.ReadFromJsonAsync<EncryptResult>())!.ciphertext;
        var c2 = (await r2.Content.ReadFromJsonAsync<EncryptResult>())!.ciphertext;

        // Different random IVs → different ciphertexts
        Assert.NotEqual(c1, c2);
    }

    [Fact]
    public async Task Encrypt_InvalidKeyLength_Returns400()
    {
        var badKey = Convert.ToBase64String(new byte[10]); // 80-bit — invalid
        var response = await _client.PostAsJsonAsync("/crypto/aes/encrypt",
            new { plaintext = "test", keyBase64 = badKey });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── Decrypt ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task EncryptThenDecrypt_RoundTrips()
    {
        var key       = await GenerateKeyAsync(256);
        var original  = "Confidential: transfer $1,000,000";

        var encResp = await _client.PostAsJsonAsync("/crypto/aes/encrypt",
            new { plaintext = original, keyBase64 = key });
        var encrypted = await encResp.Content.ReadFromJsonAsync<EncryptResult>();

        var decResp = await _client.PostAsJsonAsync("/crypto/aes/decrypt",
            new { ciphertextBase64 = encrypted!.ciphertext, ivBase64 = encrypted.iv, keyBase64 = key });
        var decrypted = await decResp.Content.ReadFromJsonAsync<DecryptResult>();

        Assert.Equal(original, decrypted!.plaintext);
    }

    [Fact]
    public async Task Decrypt_WrongKey_ThrowsOrReturnsGarbage()
    {
        var key1 = await GenerateKeyAsync(256);
        var key2 = await GenerateKeyAsync(256);

        var encResp = await _client.PostAsJsonAsync("/crypto/aes/encrypt",
            new { plaintext = "secret", keyBase64 = key1 });
        var encrypted = await encResp.Content.ReadFromJsonAsync<EncryptResult>();

        // Decrypting with wrong key should error (unpadding failure) or return wrong text
        var decResp = await _client.PostAsJsonAsync("/crypto/aes/decrypt",
            new { ciphertextBase64 = encrypted!.ciphertext, ivBase64 = encrypted.iv, keyBase64 = key2 });

        if (decResp.IsSuccessStatusCode)
        {
            var decrypted = await decResp.Content.ReadFromJsonAsync<DecryptResult>();
            Assert.NotEqual("secret", decrypted!.plaintext);
        }
        else
        {
            Assert.True((int)decResp.StatusCode >= 400);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<string> GenerateKeyAsync(int bits)
    {
        var response = await _client.GetAsync($"/crypto/aes/generate-key?bits={bits}");
        var body = await response.Content.ReadFromJsonAsync<KeyResult>();
        return body!.keyBase64;
    }

    private record KeyResult(int bits, string keyBase64);
    private record EncryptResult(string ciphertext, string iv);
    private record DecryptResult(string plaintext);
}

public class AesTestFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection;
    public AesTestFactory()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }
    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder) =>
        builder.ConfigureServices(services =>
        {
            var d = services.SingleOrDefault(s => s.ServiceType == typeof(DbContextOptions<BankingDbContext>));
            if (d is not null) services.Remove(d);
            services.AddDbContext<BankingDbContext>(o => o.UseSqlite(_connection));
            var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            scope.ServiceProvider.GetRequiredService<BankingDbContext>().Database.Migrate();
        });
    protected override void Dispose(bool disposing) { base.Dispose(disposing); if (disposing) _connection.Dispose(); }
}
