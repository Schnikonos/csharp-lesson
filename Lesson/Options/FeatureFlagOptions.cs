namespace Lesson.Options;

/// <summary>
/// Strongly-typed representation of the "FeatureFlags" configuration section.
/// </summary>
public sealed class FeatureFlagOptions
{
    public const string SectionName = "FeatureFlags";

    public bool EnableFraudDetection { get; init; }
    public bool EnableInstantPayments { get; init; }
    public bool MaintenanceMode { get; init; }
}
