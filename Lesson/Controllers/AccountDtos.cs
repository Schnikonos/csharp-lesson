using System.ComponentModel.DataAnnotations;

namespace Lesson.Controllers;

// ─── Request / Response DTOs ──────────────────────────────────────────────────
// We never expose entities directly to the API layer.
// Java parallel: @RequestBody / @ResponseBody DTO classes.

public record CreateAccountRequest(
    [Required][MaxLength(50)]  string AccountNumber,
    [Required][MaxLength(100)] string OwnerName,
    [MaxLength(20)]            string AccountType = "Checking",
    decimal                    InitialBalance = 0m
);

public record UpdateAccountRequest(
    [Required][MaxLength(100)] string OwnerName,
    [MaxLength(20)]            string AccountType,
    decimal                    Balance,
    bool                       IsActive
);

public record AccountResponse(
    int      Id,
    string   AccountNumber,
    string   OwnerName,
    string   AccountType,
    decimal  Balance,
    bool     IsActive,
    DateTime CreatedAt
);
