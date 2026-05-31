using System.ComponentModel.DataAnnotations;
using Lesson.Controllers;

namespace Lesson.Controllers;

// ── Customer DTOs ─────────────────────────────────────────────────────────────

public record CreateCustomerRequest(
    [Required][MaxLength(100)] string Name,
    [Required][MaxLength(200)][EmailAddress] string Email
);

/// <summary>
/// Response that includes the customer's accounts (populated when loaded with Include).
/// The Accounts list is empty when the customer was loaded without Include.
/// </summary>
public record CustomerResponse(
    int     Id,
    string  Name,
    string  Email,
    IReadOnlyList<AccountResponse> Accounts
);
