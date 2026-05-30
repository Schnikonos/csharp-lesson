using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Lesson.Tests;

/// <summary>
/// Integration tests for Lesson 02-B: IOptions, IOptionsSnapshot, IOptionsMonitor.
/// </summary>
public class BankSettingsControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly JsonSerializerOptions JsonOptions =
        new() { PropertyNameCaseInsensitive = true };

    // Shared test config injected for all tests in this class
    private readonly Dictionary<string, string?> _baseConfig = new()
    {
        ["Bank:Name"] = "Test Bank",
        ["Bank:SwiftCode"] = "TSTBKUS33",
        ["Bank:Country"] = "US",
        ["Bank:MaxTransferLimit"] = "5000",
        ["Bank:Contact:Email"] = "test@bank.com",
        ["Bank:Contact:Phone"] = "+1-555-0100",
        ["FeatureFlags:EnableFraudDetection"] = "true",
        ["FeatureFlags:EnableInstantPayments"] = "false",
        ["FeatureFlags:MaintenanceMode"] = "false"
    };

    private HttpClient CreateClient(
        WebApplicationFactory<Program> factory,
        Dictionary<string, string?>? overrides = null)
    {
        return factory.WithWebHostBuilder(host =>
            host.ConfigureAppConfiguration((_, config) =>
            {
                var merged = new Dictionary<string, string?>(_baseConfig);
                if (overrides is not null)
                    foreach (var kv in overrides)
                        merged[kv.Key] = kv.Value;
                config.AddInMemoryCollection(merged);
            })).CreateClient();
    }

    // -------------------------------------------------------------------------
    // GET /banksettings — IOptions<BankOptions>
    // -------------------------------------------------------------------------
    [Fact]
    public async Task Get_ReturnsBoundBankOptions()
    {
        var factory = new WebApplicationFactory<Program>();
        var client = CreateClient(factory);

        var response = await client.GetAsync("/banksettings");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        body.GetProperty("name").GetString().Should().Be("Test Bank");
        body.GetProperty("swiftCode").GetString().Should().Be("TSTBKUS33");
        body.GetProperty("maxTransferLimit").GetDecimal().Should().Be(5000m);
        body.GetProperty("contact").GetProperty("email").GetString().Should().Be("test@bank.com");
        body.GetProperty("source").GetString().Should().Be("IOptions");
    }

    // -------------------------------------------------------------------------
    // GET /banksettings/snapshot — IOptionsSnapshot<BankOptions>
    // -------------------------------------------------------------------------
    [Fact]
    public async Task GetSnapshot_ReturnsBoundValues()
    {
        var factory = new WebApplicationFactory<Program>();
        var client = CreateClient(factory);

        var response = await client.GetAsync("/banksettings/snapshot");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        body.GetProperty("name").GetString().Should().Be("Test Bank");
        body.GetProperty("source").GetString().Should().Be("IOptionsSnapshot");
    }

    // -------------------------------------------------------------------------
    // GET /banksettings/monitor — IOptionsMonitor<BankOptions>
    // -------------------------------------------------------------------------
    [Fact]
    public async Task GetMonitor_ReturnsBoundValues()
    {
        var factory = new WebApplicationFactory<Program>();
        var client = CreateClient(factory);

        var response = await client.GetAsync("/banksettings/monitor");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        body.GetProperty("name").GetString().Should().Be("Test Bank");
        body.GetProperty("source").GetString().Should().Be("IOptionsMonitor");
    }

    // -------------------------------------------------------------------------
    // GET /banksettings/flags — FeatureFlagOptions
    // -------------------------------------------------------------------------
    [Fact]
    public async Task GetFlags_ReturnsBoundFeatureFlags()
    {
        var factory = new WebApplicationFactory<Program>();
        var client = CreateClient(factory);

        var response = await client.GetAsync("/banksettings/flags");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        body.GetProperty("enableFraudDetection").GetBoolean().Should().BeTrue();
        body.GetProperty("enableInstantPayments").GetBoolean().Should().BeFalse();
    }

    // -------------------------------------------------------------------------
    // Validation: missing required field should prevent app startup
    // -------------------------------------------------------------------------
    [Fact]
    public void App_ThrowsOnMissingRequiredOption()
    {
        var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(host =>
                host.ConfigureAppConfiguration((_, config) =>
                {
                    // Clear all sources (including appsettings.json) so Bank:Name is truly absent
                    config.Sources.Clear();
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        // Bank:Name intentionally absent — triggers [Required] validation failure
                        ["Bank:SwiftCode"] = "TSTBKUS33",
                        ["Bank:Country"] = "US",
                        ["Bank:MaxTransferLimit"] = "5000",
                        ["Bank:Contact:Email"] = "test@bank.com",
                        ["FeatureFlags:EnableFraudDetection"] = "false",
                        ["FeatureFlags:EnableInstantPayments"] = "false",
                        ["FeatureFlags:MaintenanceMode"] = "false"
                    });
                }));

        // ValidateOnStart causes CreateClient() to throw during host startup
        var act = () => factory.CreateClient();
        act.Should().Throw<Exception>("Bank:Name is [Required] and was omitted");
    }
}
