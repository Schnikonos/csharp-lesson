using System.ComponentModel.DataAnnotations;

namespace Lesson.Options;

/// <summary>
/// Strongly-typed representation of the "Bank" configuration section.
/// Registered via IOptions&lt;BankOptions&gt; / IOptionsSnapshot&lt;BankOptions&gt; / IOptionsMonitor&lt;BankOptions&gt;.
/// </summary>
public sealed class BankOptions
{
    // The section key in appsettings.json this class binds to.
    public const string SectionName = "Bank";

    [Required]
    [MinLength(2)]
    public string Name { get; init; } = string.Empty;

    [Required]
    public string SwiftCode { get; init; } = string.Empty;

    [Required]
    public string Country { get; init; } = string.Empty;

    /// <summary>
    /// Must be positive. Data-annotation validation fires at startup (ValidateOnStart).
    /// </summary>
    [Range(1, double.MaxValue, ErrorMessage = "MaxTransferLimit must be > 0")]
    public decimal MaxTransferLimit { get; init; }

    // Nested section: Bank:Contact
    public ContactOptions Contact { get; init; } = new();
}

public sealed class ContactOptions
{
    [Required]
    [EmailAddress]
    public string Email { get; init; } = string.Empty;

    public string Phone { get; init; } = string.Empty;
}
