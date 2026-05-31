using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Lesson.Tests;

// =============================================================================
// LESSON 02-C TESTS: Feature Flags with Microsoft.FeatureManagement
//
// Three scenarios:
//  1. Enabled flag  — [FeatureGate] lets the request through → 200 OK.
//  2. Disabled flag — [FeatureGate] blocks the request → 404 Not Found.
//  3. Programmatic  — IFeatureManager.IsEnabledAsync drives the response body.
//
// WHY IN-MEMORY CONFIGURATION OVERRIDE?
//   WebApplicationFactory lets us inject an in-memory IConfiguration that
//   overrides only the keys we care about, keeping every other registration
//   identical to the real app.
//
// Java parallel:
//   @SpringBootTest + @TestPropertySource(properties="toggle.x=true")
//   Togglz SpringTogglzRule / FF4J test helper
// =============================================================================

public class FeatureFlagTests
{
    // =========================================================================
    // TEST 1 — [FeatureGate] passes when the flag is enabled
    //
    // Override FeatureManagement:InstantTransfer → true.
    // GET /feature-demo/instant-transfer should return 200.
    // =========================================================================
    [Fact]
    public async Task FeatureGate_EnabledFlag_Returns200()
    {
        // ----- Arrange -------------------------------------------------------
        using var factory = BuildFactory(new Dictionary<string, string?>
        {
            ["FeatureManagement:InstantTransfer"] = "true"
        });

        var client = factory.CreateClient();

        // ----- Act -----------------------------------------------------------
        var response = await client.GetAsync("/feature-demo/instant-transfer");

        // ----- Assert --------------------------------------------------------
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        body!["status"].Should().Be("enabled");
    }

    // =========================================================================
    // TEST 2 — [FeatureGate] blocks when the flag is disabled → 404
    //
    // Override FeatureManagement:InstantTransfer → false.
    // The [FeatureGate] attribute short-circuits and returns 404.
    //
    // Java parallel:
    //   @TogglzRule.disable(Feature.INSTANT_TRANSFER) → 404/403 on guarded endpoint
    // =========================================================================
    [Fact]
    public async Task FeatureGate_DisabledFlag_Returns404()
    {
        // ----- Arrange -------------------------------------------------------
        using var factory = BuildFactory(new Dictionary<string, string?>
        {
            ["FeatureManagement:InstantTransfer"] = "false"
        });

        var client = factory.CreateClient();

        // ----- Act -----------------------------------------------------------
        var response = await client.GetAsync("/feature-demo/instant-transfer");

        // ----- Assert --------------------------------------------------------
        // [FeatureGate] returns 404 when the feature is disabled
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // =========================================================================
    // TEST 3 — Programmatic IFeatureManager drives the response body
    //
    // When EnhancedStatements is disabled the body contains status=disabled
    // and a null data field — no hard 404, just a graceful degradation.
    //
    // Java parallel:
    //   FeatureManager.isActive(MyFeatures.ENHANCED_STATEMENTS) branching
    // =========================================================================
    [Fact]
    public async Task ProgrammaticCheck_DisabledFlag_ReturnsDisabledBody()
    {
        // ----- Arrange -------------------------------------------------------
        using var factory = BuildFactory(new Dictionary<string, string?>
        {
            ["FeatureManagement:EnhancedStatements"] = "false"
        });

        var client = factory.CreateClient();

        // ----- Act -----------------------------------------------------------
        var response = await client.GetAsync("/feature-demo/enhanced-statements");

        // ----- Assert --------------------------------------------------------
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, object?>>();
        body!["status"].ToString().Should().Be("disabled");
    }

    // =========================================================================
    // TEST 4 — /all endpoint lists every known flag
    // =========================================================================
    [Fact]
    public async Task AllEndpoint_ReturnsAllFlags()
    {
        // ----- Arrange -------------------------------------------------------
        using var factory = BuildFactory(new Dictionary<string, string?>
        {
            ["FeatureManagement:InstantTransfer"]    = "true",
            ["FeatureManagement:EnhancedStatements"] = "false",
            ["FeatureManagement:DarkMode"]           = "false"
        });

        var client = factory.CreateClient();

        // ----- Act -----------------------------------------------------------
        var response = await client.GetAsync("/feature-demo/all");

        // ----- Assert --------------------------------------------------------
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, bool>>();
        body.Should().ContainKey("InstantTransfer").And.ContainKey("EnhancedStatements").And.ContainKey("DarkMode");
        body!["InstantTransfer"].Should().BeTrue();
        body["EnhancedStatements"].Should().BeFalse();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helper: spin up the full app pipeline with in-memory config overrides.
    // ─────────────────────────────────────────────────────────────────────────
    private static WebApplicationFactory<Program> BuildFactory(
        Dictionary<string, string?> configOverrides) =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(host =>
                host.ConfigureAppConfiguration((_, cfg) =>
                    cfg.AddInMemoryCollection(configOverrides)));
}
