using Asp.Versioning;
using Lesson.Data;
using Lesson.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Lesson.Controllers;

/// <summary>
/// Lesson 21-B — API versioning with Asp.Versioning.
///
/// Two version of the accounts API:
///   v1 — returns a flat DTO (AccountSummaryV1)
///   v2 — returns an enriched DTO with type and soft-delete flag (AccountSummaryV2)
///
/// Version is selected via URL segment: /v{n}/versioned/accounts
///
/// Java parallel:
///   Spring Boot @ApiVersion / spring-doc springdoc-openapi versioning
///   @RequestMapping("/v1/...") and "/v2/..." on separate controllers
/// </summary>
[ApiController]
[Route("v{version:apiVersion}/versioned/accounts")]
[ApiVersion(1)]
[ApiVersion(2)]
public class VersionedAccountsController(BankingDbContext db) : ControllerBase
{
    // GET v1/versioned/accounts — basic projection
    [HttpGet]
    [MapToApiVersion(1)]
    public async Task<IActionResult> GetV1(CancellationToken ct)
    {
        var list = await db.BankAccounts
            .Where(a => !a.IsDeleted)
            .Select(a => new AccountSummaryV1(a.Id, a.AccountNumber, a.OwnerName, a.Balance))
            .ToListAsync(ct);
        return Ok(list);
    }

    // GET v2/versioned/accounts — enriched projection
    [HttpGet]
    [MapToApiVersion(2)]
    public async Task<IActionResult> GetV2(CancellationToken ct)
    {
        var list = await db.BankAccounts
            .Where(a => !a.IsDeleted)
            .Select(a => new AccountSummaryV2(a.Id, a.AccountNumber, a.OwnerName, a.Balance,
                                              a.AccountType, a.IsDeleted))
            .ToListAsync(ct);
        return Ok(list);
    }

    // POST v1/versioned/accounts — create (same logic both versions)
    [HttpPost]
    [MapToApiVersion(1)]
    [MapToApiVersion(2)]
    public async Task<IActionResult> Create([FromBody] CreateVersionedAccountRequest req, CancellationToken ct)
    {
        var account = new BankAccount
        {
            AccountNumber = req.AccountNumber,
            OwnerName     = req.OwnerName,
            Balance       = req.InitialBalance,
            AccountType   = "Savings",
        };
        db.BankAccounts.Add(account);
        await db.SaveChangesAsync(ct);
        return Created($"/v1/versioned/accounts/{account.Id}", new { account.Id });
    }
}

public record AccountSummaryV1(int Id, string AccountNumber, string OwnerName, decimal Balance);
public record AccountSummaryV2(int Id, string AccountNumber, string OwnerName, decimal Balance,
    string AccountType, bool IsDeleted);
public record CreateVersionedAccountRequest(string AccountNumber, string OwnerName, decimal InitialBalance);
