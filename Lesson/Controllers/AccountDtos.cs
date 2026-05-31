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

// ── Lesson 04-B DTOs ──────────────────────────────────────────────────────────

/// <summary>
/// Lightweight projection — only the columns needed for a list view.
/// Using Select() to build this in SQL avoids fetching audit, RowVersion, Address columns.
/// Java parallel: a DTO interface projection or @SqlResultSetMapping in JPA.
/// </summary>
public record AccountSummaryDto(
    int     Id,
    string  AccountNumber,
    string  OwnerName,
    string  AccountType,
    decimal Balance,
    bool    IsActive
);

/// <summary>
/// Result of a GroupBy aggregate query: per-account-type statistics.
/// Java parallel: a custom JPQL result class / DTO projection constructor expression.
/// </summary>
public record AccountTypeStatDto(
    string  AccountType,
    int     Count,
    decimal TotalBalance,
    double  AverageBalance
);

/// <summary>
/// Generic pagination envelope.
/// Java parallel: Spring Data's Page&lt;T&gt; interface.
/// </summary>
public record PagedResult<T>(
    IReadOnlyList<T> Items,
    int              TotalCount,
    int              Page,
    int              PageSize
)
{
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
    public bool HasNextPage => Page < TotalPages;
    public bool HasPreviousPage => Page > 1;
};

// ─── Lesson 04-C DTOs ─────────────────────────────────────────────────────────

/// <summary>
/// Lightweight projection of a <c>Transaction</c> row — used by the split-query
/// and raw-SQL endpoints so that full entity graphs are never sent over the wire.
/// </summary>
public record TransactionSummaryDto(
    int     Id,
    string  Type,
    decimal Amount,
    string  Description,
    DateTime OccurredAt
);
