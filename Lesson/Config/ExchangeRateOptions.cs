namespace Lesson.Config;

// -----------------------------------------------------------------------------
// C# NOTE: "Options pattern" — strongly-typed configuration.
//
// This plain class maps 1-to-1 to a JSON section in appsettings.json.
// ASP.NET Core binds the JSON values to this class at startup and makes it
// available anywhere via IOptions<ExchangeRateOptions>.
//
// Java parallel:
//   @ConfigurationProperties(prefix = "exchange-rate")
//   public class ExchangeRateProperties { private String baseUrl; ... }
//   → public class ExchangeRateOptions { public string BaseUrl { get; set; } }
//
// Naming convention: suffix the class with "Options" and match the JSON
// section name exactly (case-insensitive).
// -----------------------------------------------------------------------------

public class ExchangeRateOptions
{
    // The section name used in appsettings.json and in Program.cs registration.
    public const string SectionName = "ExchangeRate";

    public string BaseUrl { get; set; } = string.Empty;

    // Timeout for outgoing HTTP calls in seconds.
    // Having it in config means ops can tune it without a code change.
    public int TimeoutSeconds { get; set; } = 10;
}
