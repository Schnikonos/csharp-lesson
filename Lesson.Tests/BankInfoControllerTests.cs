using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Lesson.Tests;

// =============================================================================
// INTEGRATION TESTS: BankInfoController
//
// We use WebApplicationFactory<Program> to spin up the full ASP.NET Core
// pipeline in-process (no real TCP server). This lets us test the controller
// together with the real configuration system.
//
// KEY TECHNIQUE — WithWebHostBuilder + AddInMemoryCollection
//   In tests we do NOT want to rely on the real appsettings.json values, which
//   could change. Instead we inject deterministic config via AddInMemoryCollection.
//   This is the recommended way to supply test config in ASP.NET Core.
//
// Java parallel:
//   @SpringBootTest + TestPropertySource / @MockBean
//   → WebApplicationFactory + WithWebHostBuilder + AddInMemoryCollection
// =============================================================================

public class BankInfoControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
    // Shared config injected into the test host — acts as our "test appsettings".
    private static readonly Dictionary<string, string?> TestConfig = new()
    {
        ["Bank:Name"]             = "Test Bank",
        ["Bank:SwiftCode"]        = "TSTGB2L",
        ["Bank:Country"]          = "UK",
        ["Bank:MaxTransferLimit"] = "9999",
        ["Bank:Contact:Email"]    = "test@bank.com",
        ["FeatureFlags:EnableFraudDetection"]  = "true",
        ["FeatureFlags:EnableInstantPayments"] = "false",
        ["FeatureFlags:MaintenanceMode"]       = "true",
    };

    private HttpClient CreateClient()
    {
        var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(host =>
                host.ConfigureAppConfiguration((_, config) =>
                    // AddInMemoryCollection is added LAST so it wins over
                    // appsettings.json values — same override ordering rule
                    // as environment variables, but in-memory.
                    config.AddInMemoryCollection(TestConfig)));

        return factory.CreateClient();
    }

    // =========================================================================
    // TEST 1 — GET /bankinfo returns 200 with expected values
    // =========================================================================
    [Fact]
    public async Task Get_ReturnsBankInfo_WithExpectedValues()
    {
        // ----- Arrange -------------------------------------------------------
        var client = CreateClient();

        // ----- Act -----------------------------------------------------------
        var response = await client.GetAsync("/bankinfo");

        // ----- Assert --------------------------------------------------------
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json    = await response.Content.ReadAsStringAsync();
        var doc     = JsonDocument.Parse(json).RootElement;

        // C# System.Text.Json deserializes to camelCase by default in ASP.NET Core.
        doc.GetProperty("name").GetString().Should().Be("Test Bank");
        doc.GetProperty("swiftCode").GetString().Should().Be("TSTGB2L");
        doc.GetProperty("country").GetString().Should().Be("UK");
        doc.GetProperty("contactEmail").GetString().Should().Be("test@bank.com");
        doc.GetProperty("maxTransferLimit").GetDecimal().Should().Be(9999m);
    }

    // =========================================================================
    // TEST 2 — GET /bankinfo/features returns all feature flags as booleans
    // =========================================================================
    [Fact]
    public async Task GetFeatureFlags_ReturnsAllFlags()
    {
        // ----- Arrange -------------------------------------------------------
        var client = CreateClient();

        // ----- Act -----------------------------------------------------------
        var response = await client.GetAsync("/bankinfo/features");

        // ----- Assert --------------------------------------------------------
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json  = await response.Content.ReadAsStringAsync();
        var flags = JsonSerializer.Deserialize<Dictionary<string, bool>>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        flags["EnableFraudDetection"].Should().BeTrue();
        flags["EnableInstantPayments"].Should().BeFalse();
        flags["MaintenanceMode"].Should().BeTrue();
    }

    // =========================================================================
    // TEST 3 — GET /bankinfo/environment returns environment info
    // =========================================================================
    [Fact]
    public async Task GetEnvironment_ReturnsEnvironmentName()
    {
        // ----- Arrange -------------------------------------------------------
        var client = CreateClient();

        // ----- Act -----------------------------------------------------------
        var response = await client.GetAsync("/bankinfo/environment");

        // ----- Assert --------------------------------------------------------
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        var doc  = JsonDocument.Parse(json).RootElement;

        // WebApplicationFactory sets the environment to "Development" by default.
        doc.GetProperty("name").GetString().Should().Be("Development");
        doc.GetProperty("isDevelopment").GetBoolean().Should().BeTrue();
        doc.GetProperty("isProduction").GetBoolean().Should().BeFalse();
        // Our test config sets MaxTransferLimit = 9999
        doc.GetProperty("maxTransferLimit").GetDecimal().Should().Be(9999m);
    }

    // =========================================================================
    // TEST 4 — Config override: MaxTransferLimit can be changed per-test
    //
    // This test demonstrates that AddInMemoryCollection values take precedence
    // over appsettings.json — the core point of Lesson 02-A.
    // =========================================================================
    [Fact]
    public async Task Get_WithOverriddenConfig_ReturnsOverriddenValue()
    {
        // ----- Arrange — override MaxTransferLimit for just this test --------
        var customConfig = new Dictionary<string, string?>
        {
            ["Bank:Name"]             = "Override Bank",
            ["Bank:SwiftCode"]        = "OVRGB2L",
            ["Bank:Country"]          = "FR",
            ["Bank:MaxTransferLimit"] = "1",       // ← overridden to 1
            ["Bank:Contact:Email"]    = "override@bank.com",
        };

        var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(host =>
                host.ConfigureAppConfiguration((_, config) =>
                    config.AddInMemoryCollection(customConfig)));

        var client = factory.CreateClient();

        // ----- Act -----------------------------------------------------------
        var response = await client.GetAsync("/bankinfo");

        // ----- Assert --------------------------------------------------------
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        var doc  = JsonDocument.Parse(json).RootElement;

        doc.GetProperty("name").GetString().Should().Be("Override Bank");
        doc.GetProperty("maxTransferLimit").GetDecimal().Should().Be(1m);
    }
}
