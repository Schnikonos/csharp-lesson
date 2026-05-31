using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Lesson.Data;

namespace Lesson.Tests;

/// <summary>
/// Lesson 11-C integration tests — ASP.NET Core Data Protection API.
/// </summary>
public class DataProtectionTests : IClassFixture<DataProtectionTestFactory>
{
    private readonly HttpClient _client;
    public DataProtectionTests(DataProtectionTestFactory factory) => _client = factory.CreateClient();

    // ── Protect / Unprotect ───────────────────────────────────────────────────

    [Fact]
    public async Task Protect_Returns200_WithToken()
    {
        var response = await _client.PostAsJsonAsync("/crypto/dp/protect",
            new { plaintext = "sensitive-data", purpose = "test" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ProtectResult>();
        Assert.False(string.IsNullOrEmpty(body!.token));
        Assert.NotEqual("sensitive-data", body.token);
    }

    [Fact]
    public async Task ProtectThenUnprotect_RoundTrips()
    {
        const string original = "AccountId:99999";

        var pResp = await _client.PostAsJsonAsync("/crypto/dp/protect",
            new { plaintext = original, purpose = "account-id" });
        var token = (await pResp.Content.ReadFromJsonAsync<ProtectResult>())!.token;

        var uResp = await _client.PostAsJsonAsync("/crypto/dp/unprotect",
            new { token, purpose = "account-id" });
        var body = await uResp.Content.ReadFromJsonAsync<UnprotectResult>();

        Assert.Equal(original, body!.plaintext);
    }

    [Fact]
    public async Task Unprotect_WrongPurpose_Returns400()
    {
        var pResp = await _client.PostAsJsonAsync("/crypto/dp/protect",
            new { plaintext = "secret", purpose = "purpose-a" });
        var token = (await pResp.Content.ReadFromJsonAsync<ProtectResult>())!.token;

        // Attempt to unprotect with a different purpose — must fail
        var uResp = await _client.PostAsJsonAsync("/crypto/dp/unprotect",
            new { token, purpose = "purpose-b" });
        Assert.Equal(HttpStatusCode.BadRequest, uResp.StatusCode);
    }

    [Fact]
    public async Task Unprotect_TamperedToken_Returns400()
    {
        var pResp = await _client.PostAsJsonAsync("/crypto/dp/protect",
            new { plaintext = "data", purpose = "tamper-test" });
        var token = (await pResp.Content.ReadFromJsonAsync<ProtectResult>())!.token;

        // Append garbage to the token to simulate tampering
        var tampered = token + "AAAA";
        var uResp    = await _client.PostAsJsonAsync("/crypto/dp/unprotect",
            new { token = tampered, purpose = "tamper-test" });
        Assert.Equal(HttpStatusCode.BadRequest, uResp.StatusCode);
    }

    [Fact]
    public async Task Protect_SamePlaintext_ProducesDifferentTokens()
    {
        var r1 = await _client.PostAsJsonAsync("/crypto/dp/protect",
            new { plaintext = "same", purpose = "p" });
        var r2 = await _client.PostAsJsonAsync("/crypto/dp/protect",
            new { plaintext = "same", purpose = "p" });

        var t1 = (await r1.Content.ReadFromJsonAsync<ProtectResult>())!.token;
        var t2 = (await r2.Content.ReadFromJsonAsync<ProtectResult>())!.token;

        // Data Protection adds a random nonce — identical plaintext yields different tokens
        Assert.NotEqual(t1, t2);
    }

    // ── Time-limited tokens ───────────────────────────────────────────────────

    [Fact]
    public async Task CreateToken_Returns200_WithExpiryAndToken()
    {
        var response = await _client.PostAsJsonAsync("/crypto/dp/token/create",
            new { payload = "user@bank.com", ttlSeconds = 300 });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<TokenCreateResult>();
        Assert.False(string.IsNullOrEmpty(body!.token));
        Assert.True(body.expiresAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task CreateAndConsumeToken_RoundTrips()
    {
        const string email = "reset@acmebank.com";

        var createResp = await _client.PostAsJsonAsync("/crypto/dp/token/create",
            new { payload = email, ttlSeconds = 60 });
        var token = (await createResp.Content.ReadFromJsonAsync<TokenCreateResult>())!.token;

        var consumeResp = await _client.PostAsJsonAsync("/crypto/dp/token/consume",
            new { token });
        var body = await consumeResp.Content.ReadFromJsonAsync<TokenConsumeResult>();

        Assert.Equal(email, body!.payload);
    }

    [Fact]
    public async Task ConsumeToken_Expired_Returns400()
    {
        // TTL of 1 second — then wait for it to expire
        var createResp = await _client.PostAsJsonAsync("/crypto/dp/token/create",
            new { payload = "expires-soon", ttlSeconds = 1 });
        var token = (await createResp.Content.ReadFromJsonAsync<TokenCreateResult>())!.token;

        await Task.Delay(TimeSpan.FromSeconds(2));

        var consumeResp = await _client.PostAsJsonAsync("/crypto/dp/token/consume",
            new { token });
        Assert.Equal(HttpStatusCode.BadRequest, consumeResp.StatusCode);
    }

    // ── Response shapes ───────────────────────────────────────────────────────
    private record ProtectResult(string token, string purpose);
    private record UnprotectResult(string plaintext);
    private record TokenCreateResult(string token, DateTimeOffset expiresAt);
    private record TokenConsumeResult(string payload, DateTimeOffset expiresAt);
}

public class DataProtectionTestFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection;
    public DataProtectionTestFactory()
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
