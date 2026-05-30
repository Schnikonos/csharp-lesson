using System.ComponentModel.DataAnnotations;

namespace Lesson.Controllers;

// ─── Request / Response DTOs ──────────────────────────────────────────────────
// We never expose entities directly to the API layer.
// Java parallel: @RequestBody / @ResponseBody DTO classes.

/// <summary>Lesson 03-B — address sub-DTO (mirrors the owned entity).</summary>
public record AddressDto(
    [MaxLength(200)] string Street,
    [MaxLength(100)] string City,
    [MaxLength(20)]  string PostalCode,
    [MaxLength(60)]  string Country
);

public record CreateAccountRequest(
    [Required][MaxLength(50)]  string AccountNumber,
    [Required][MaxLength(100)] string OwnerName,
    [MaxLength(20)]            string AccountType = "Checking",
    decimal                    InitialBalance = 0m,
    AddressDto?                Address = null
);

public record UpdateAccountRequest(
    [Required][MaxLength(100)] string OwnerName,
    [MaxLength(20)]            string AccountType,
    decimal                    Balance,
    bool                       IsActive,
    AddressDto?                Address = null
);

public record AccountResponse(
    int          Id,
    string       AccountNumber,
    string       OwnerName,
    string       AccountType,
    decimal      Balance,
    bool         IsActive,
    DateTime     CreatedAt,
    DateTime?    UpdatedAt,
    string?      UpdatedBy,
    AddressDto?  Address
);
