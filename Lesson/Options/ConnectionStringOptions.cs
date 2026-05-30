using System.ComponentModel.DataAnnotations;

namespace Lesson.Options;

/// <summary>
/// Bound to the "ConnectionStrings" section.
/// Connection strings should NEVER be committed — use User Secrets (dev) or
/// environment variables (prod): ConnectionStrings__BankDb=...
///
/// Java parallel: application.properties datasource.url + @ConfigurationProperties
/// </summary>
public sealed class ConnectionStringOptions
{
    public const string SectionName = "ConnectionStrings";

    /// <summary>
    /// Set via User Secrets: dotnet user-secrets set "ConnectionStrings:BankDb" "..."
    /// Set via env-var:      ConnectionStrings__BankDb=...   (double underscore = colon)
    /// </summary>
    [Required]
    public string BankDb { get; init; } = string.Empty;

    public string AuditDb { get; init; } = string.Empty;
}
