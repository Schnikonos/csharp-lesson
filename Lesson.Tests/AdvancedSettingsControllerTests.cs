using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Lesson.Tests;

/// <summary>
/// Integration tests for Lesson 02-C: User Secrets simulation, Named Options, Custom Provider.
///
/// We cannot rely on the real User Secrets store in CI — instead we inject
/// connection strings via AddInMemoryCollection, just like secrets would be
/// layered in at runtime.
/// </summary>
public class AdvancedSettingsControllerTests
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Creates a test client with all required config keys injected in-memory.</summary>
    private static HttpClient CreateClient(Dictionary<string, string?>? overrides = null)
    {
        var config = new Dictionary<string, string?>
        {
            // Bank (required by BankOptions + ValidateOnStart)
            ["Bank:Name"] = "Test Bank",
            ["Bank:SwiftCode"] = "TSTBKUS33",
            ["Bank:Country"] = "US",
            ["Bank:MaxTransferLimit"] = "5000",
            ["Bank:Contact:Email"] = "test@bank.com",
            ["Bank:Contact:Phone"] = "+1-555-0100",
            // Feature flags
            ["FeatureFlags:EnableFraudDetection"] = "false",
            ["FeatureFlags:EnableInstantPayments"] = "false",
            ["FeatureFlags:MaintenanceMode"] = "false",
            // Connection strings (simulates User Secrets / env-vars)
            ["ConnectionStrings:BankDb"] = "Server=test-db;Database=BankTest;User Id=ci;Password=ci;",
            ["ConnectionStrings:AuditDb"] = "Server=test-audit;Database=AuditTest;User Id=ci;Password=ci;",
            // Named options
            ["TransferLimits:Domestic:DailyLimit"] = "10000",
            ["TransferLimits:Domestic:SingleTransactionLimit"] = "5000",
            ["TransferLimits:Domestic:Currency"] = "USD",
            ["TransferLimits:International:DailyLimit"] = "3000",
            ["TransferLimits:International:SingleTransactionLimit"] = "1000",
            ["TransferLimits:International:Currency"] = "USD",
            // Custom provider keys — in tests we supply these directly via in-memory
            // config (the real InMemoryDbConfigurationProvider is cleared with Sources.Clear()).
            // The controller reads IConfiguration["CustomConfig:*"] regardless of source.
            ["CustomConfig:WelcomeMessage"] = "Hello from the custom provider!",
            ["CustomConfig:MaxRetries"] = "3",
            ["CustomConfig:ServiceUrl"] = "https://api.acmebank.internal"
        };

        if (overrides is not null)
            foreach (var kv in overrides)
                config[kv.Key] = kv.Value;

        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(host =>
                host.ConfigureAppConfiguration((_, builder) =>
                {
                    builder.Sources.Clear();
                    builder.AddInMemoryCollection(config);
                }))
            .CreateClient();
    }

    // -------------------------------------------------------------------------
    // GET /advancedsettings/connection — connection strings from "secrets"
    // -------------------------------------------------------------------------
    [Fact]
    public async Task GetConnection_ReturnsServerNameOnly()
    {
        var client = CreateClient();

        var response = await client.GetAsync("/advancedsettings/connection");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        body.GetProperty("bankDbServer").GetString().Should().Be("test-db");
        body.GetProperty("auditDbServer").GetString().Should().Be("test-audit");
        // Confirm that passwords are NOT exposed
        var raw = await response.Content.ReadAsStringAsync();
        raw.Should().NotContain("ci;", because: "passwords must never appear in the response");
    }

    // -------------------------------------------------------------------------
    // GET /advancedsettings/limits/domestic — named options
    // -------------------------------------------------------------------------
    [Fact]
    public async Task GetLimits_Domestic_ReturnsDomesticLimits()
    {
        var client = CreateClient();

        var response = await client.GetAsync("/advancedsettings/limits/domestic");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        body.GetProperty("dailyLimit").GetDecimal().Should().Be(10000m);
        body.GetProperty("singleTransactionLimit").GetDecimal().Should().Be(5000m);
    }

    // -------------------------------------------------------------------------
    // GET /advancedsettings/limits/international — named options
    // -------------------------------------------------------------------------
    [Fact]
    public async Task GetLimits_International_ReturnsInternationalLimits()
    {
        var client = CreateClient();

        var response = await client.GetAsync("/advancedsettings/limits/international");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        body.GetProperty("dailyLimit").GetDecimal().Should().Be(3000m);
        body.GetProperty("singleTransactionLimit").GetDecimal().Should().Be(1000m);
    }

    // -------------------------------------------------------------------------
    // GET /advancedsettings/limits/unknown — bad name returns 400
    // -------------------------------------------------------------------------
    [Fact]
    public async Task GetLimits_UnknownName_ReturnsBadRequest()
    {
        var client = CreateClient();

        var response = await client.GetAsync("/advancedsettings/limits/unknown");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // -------------------------------------------------------------------------
    // GET /advancedsettings/custom/{key} — custom provider
    // -------------------------------------------------------------------------
    [Fact]
    public async Task GetCustom_KnownKey_ReturnsValueFromCustomProvider()
    {
        var client = CreateClient();

        var response = await client.GetAsync("/advancedsettings/custom/WelcomeMessage");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        body.GetProperty("value").GetString().Should().Be("Hello from the custom provider!");
    }

    // -------------------------------------------------------------------------
    // GET /advancedsettings/custom/missing — unknown key returns 404
    // -------------------------------------------------------------------------
    [Fact]
    public async Task GetCustom_UnknownKey_ReturnsNotFound()
    {
        var client = CreateClient();

        var response = await client.GetAsync("/advancedsettings/custom/DoesNotExist");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
