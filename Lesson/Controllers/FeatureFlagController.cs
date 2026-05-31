using Microsoft.AspNetCore.Mvc;
using Microsoft.FeatureManagement;
using Microsoft.FeatureManagement.Mvc;

namespace Lesson.Controllers;

/// <summary>
/// Lesson 02-C — Feature Flags with Microsoft.FeatureManagement.
///
/// Feature flags decouple deployment from release: code ships to production
/// but features are toggled on/off via configuration — no redeploy needed.
///
/// Java parallel:
///   Togglz / FF4J / LaunchDarkly  →  Microsoft.FeatureManagement
///   @TogglzFeatureActive           →  [FeatureGate("FeatureName")]
///   FeatureManager.isActive(...)   →  IFeatureManager.IsEnabledAsync(...)
/// </summary>
[ApiController]
[Route("feature-demo")]
public class FeatureFlagController(IFeatureManager features) : ControllerBase
{
    /// <summary>
    /// GET /feature-demo/instant-transfer
    ///
    /// [FeatureGate] short-circuits the action and returns 404 when the feature
    /// is disabled — no code inside the action body needed.
    ///
    /// Java parallel:
    ///   @TogglzFeatureActive / LaunchDarkly attribute-based feature gate
    /// </summary>
    [HttpGet("instant-transfer")]
    [FeatureGate(FeatureFlags.InstantTransfer)]
    public IActionResult InstantTransfer() =>
        Ok(new { feature = FeatureFlags.InstantTransfer, status = "enabled" });

    /// <summary>
    /// GET /feature-demo/enhanced-statements
    ///
    /// Programmatic check with IFeatureManager — allows richer branching
    /// (e.g. return partial data rather than a hard 404).
    ///
    /// Java parallel:
    ///   FeatureManager.isActive(MyFeatures.ENHANCED_STATEMENTS)
    /// </summary>
    [HttpGet("enhanced-statements")]
    public async Task<IActionResult> EnhancedStatements(CancellationToken ct)
    {
        if (!await features.IsEnabledAsync(FeatureFlags.EnhancedStatements, ct))
            return Ok(new { feature = FeatureFlags.EnhancedStatements, status = "disabled", data = (object?)null });

        return Ok(new { feature = FeatureFlags.EnhancedStatements, status = "enabled", data = new[] { "txn-1", "txn-2" } });
    }

    /// <summary>
    /// GET /feature-demo/all
    /// Returns the current enabled/disabled state of all known feature flags.
    /// Useful for a feature-flag dashboard endpoint.
    /// </summary>
    [HttpGet("all")]
    public async Task<IActionResult> AllFlags(CancellationToken ct)
    {
        var states = new Dictionary<string, bool>();
        foreach (var flag in FeatureFlags.All)
            states[flag] = await features.IsEnabledAsync(flag, ct);
        return Ok(states);
    }
}

/// <summary>
/// Central registry of feature flag names.
/// Using constants avoids string literals scattered across the codebase.
/// Java parallel: Togglz Feature enum / LaunchDarkly flag key constants.
/// </summary>
public static class FeatureFlags
{
    public const string InstantTransfer   = "InstantTransfer";
    public const string EnhancedStatements = "EnhancedStatements";
    public const string DarkMode           = "DarkMode";

    public static readonly string[] All = [InstantTransfer, EnhancedStatements, DarkMode];
}
