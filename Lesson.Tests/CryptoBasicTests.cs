using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Lesson.Data;

namespace Lesson.Tests;

/// <summary>
/// Lesson 11-A tests — Base64, SHA-256, HMACSHA256, BCrypt.
/// These are unit-style tests exercised through the HTTP API.
/// </summary>
public class CryptoBasicTests : IClassFixture<CryptoTestFactory>
{
    private readonly HttpClient _client;
    public CryptoBasicTests(CryptoTestFactory factory) => _client = factory.CreateClient();

    // ── Base64 ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Encode_ReturnsBase64()
    {
        var response = await _client.PostAsJsonAsync("/crypto/base64/encode", new { text = "Hello, Bank!" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<EncodedResult>();
        Assert.Equal("SGVsbG8sIEJhbmsh", body!.encoded);
    }

    [Fact]
    public async Task Decode_ReturnsOriginalText()
    {
        var response = await _client.PostAsJsonAsync("/crypto/base64/decode", new { text = "SGVsbG8sIEJhbmsh" });
        var body = await response.Content.ReadFromJsonAsync<DecodedResult>();
        Assert.Equal("Hello, Bank!", body!.decoded);
    }

    [Fact]
    public async Task Decode_InvalidBase64_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/crypto/base64/decode", new { text = "!!not-base64!!" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── SHA-256 ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Sha256_ReturnsKnownHash()
    {
        var response = await _client.PostAsJsonAsync("/crypto/sha256", new { text = "abc" });
        var body = await response.Content.ReadFromJsonAsync<HashResult>();
        // SHA-256("abc") is well-known
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", body!.hex);
    }

    [Fact]
    public async Task Sha256_DifferentInputs_ProduceDifferentHashes()
    {
        var r1 = await _client.PostAsJsonAsync("/crypto/sha256", new { text = "password" });
        var r2 = await _client.PostAsJsonAsync("/crypto/sha256", new { text = "Password" });
        var h1 = (await r1.Content.ReadFromJsonAsync<HashResult>())!.hex;
        var h2 = (await r2.Content.ReadFromJsonAsync<HashResult>())!.hex;
        Assert.NotEqual(h1, h2);
    }

    // ── HMAC ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Hmac_SameKeyAndData_ProducesSameResult()
    {
        var payload = new { key = "secret", data = "transaction:42" };
        var r1 = await _client.PostAsJsonAsync("/crypto/hmac", payload);
        var r2 = await _client.PostAsJsonAsync("/crypto/hmac", payload);
        var h1 = (await r1.Content.ReadFromJsonAsync<HashResult>())!.hex;
        var h2 = (await r2.Content.ReadFromJsonAsync<HashResult>())!.hex;
        Assert.Equal(h1, h2);
    }

    [Fact]
    public async Task HmacVerify_CorrectHex_ReturnsValid()
    {
        var hmacResp = await _client.PostAsJsonAsync("/crypto/hmac",
            new { key = "k", data = "d" });
        var computed = (await hmacResp.Content.ReadFromJsonAsync<HashResult>())!.hex;

        var verifyResp = await _client.PostAsJsonAsync("/crypto/hmac/verify",
            new { key = "k", data = "d", expectedHex = computed });
        var body = await verifyResp.Content.ReadFromJsonAsync<ValidResult>();
        Assert.True(body!.valid);
    }

    [Fact]
    public async Task HmacVerify_WrongHex_ReturnsInvalid()
    {
        var verifyResp = await _client.PostAsJsonAsync("/crypto/hmac/verify",
            new { key = "k", data = "d", expectedHex = new string('0', 64) });
        var body = await verifyResp.Content.ReadFromJsonAsync<ValidResult>();
        Assert.False(body!.valid);
    }

    // ── BCrypt ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task HashPassword_ProducesValidBcryptHash()
    {
        var response = await _client.PostAsJsonAsync("/crypto/password/hash", new { password = "S3cur3P@ss" });
        var body = await response.Content.ReadFromJsonAsync<HashedResult>();
        // BCrypt hashes start with $2a$ or $2b$
        Assert.StartsWith("$2", body!.hash);
    }

    [Fact]
    public async Task VerifyPassword_CorrectPassword_ReturnsValid()
    {
        var hashResp = await _client.PostAsJsonAsync("/crypto/password/hash", new { password = "MyPassword" });
        var hashed = (await hashResp.Content.ReadFromJsonAsync<HashedResult>())!.hash;

        var verifyResp = await _client.PostAsJsonAsync("/crypto/password/verify",
            new { password = "MyPassword", hash = hashed });
        var body = await verifyResp.Content.ReadFromJsonAsync<ValidResult>();
        Assert.True(body!.valid);
    }

    [Fact]
    public async Task VerifyPassword_WrongPassword_ReturnsInvalid()
    {
        var hashResp = await _client.PostAsJsonAsync("/crypto/password/hash", new { password = "correct" });
        var hashed = (await hashResp.Content.ReadFromJsonAsync<HashedResult>())!.hash;

        var verifyResp = await _client.PostAsJsonAsync("/crypto/password/verify",
            new { password = "wrong", hash = hashed });
        var body = await verifyResp.Content.ReadFromJsonAsync<ValidResult>();
        Assert.False(body!.valid);
    }

    // ── Response shapes ───────────────────────────────────────────────────────
    private record EncodedResult(string encoded);
    private record DecodedResult(string decoded);
    private record HashResult(string hex, string base64);
    private record HashedResult(string hash);
    private record ValidResult(bool valid);
}

public class CryptoTestFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection;
    public CryptoTestFactory()
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
