using Lesson.Data;
using Lesson.Domain;
using Lesson.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Lesson.Controllers;

/// <summary>
/// Lesson 19-C — Bounded context, post-commit domain events, and optimistic concurrency.
///
/// This controller demonstrates:
///   1. Post-commit event dispatch via AggregateUnitOfWork
///   2. Optimistic concurrency: ETag (RowVersion) returned on GET, sent back on update
///   3. Conflict handling returning HTTP 409
///
/// Java parallel:
///   Spring @RestController returning ETag via ResponseEntity.eTag(...)
///   JPA OptimisticLockException → 409 Conflict
/// </summary>
[ApiController]
[Route("ddd/advanced/accounts")]
public class DddAdvancedController(
    IAggregateRepository  repo,
    AggregateUnitOfWork   uow,
    BankingDbContext       db) : ControllerBase
{
    // POST /ddd/advanced/accounts — open account, dispatch domain event post-commit
    [HttpPost]
    public async Task<IActionResult> Open([FromBody] OpenAdvancedRequest req, CancellationToken ct)
    {
        var agg = BankAccountAggregate.Open(req.AccountNumber, req.OwnerName,
            new Money(req.InitialBalance, "USD"));

        db.BankAccounts.Add(MapToEntity(agg));
        await uow.CommitAsync([agg], ct);          // domain events dispatched after commit

        // Fetch the DB-assigned id by re-querying
        var entity = await db.BankAccounts
            .FirstAsync(a => a.AccountNumber == req.AccountNumber, ct);

        return CreatedAtAction(nameof(GetById), new { id = entity.Id }, new { entity.Id });
    }

    // GET /ddd/advanced/accounts/{id} — returns account + ETag for optimistic concurrency
    //
    // Lesson 19-C note on ETag strategy:
    //   SQL Server  → use [Timestamp] RowVersion byte[] (DB-managed)
    //   SQLite / any  → encode relevant mutable state (e.g. Balance) as the version token
    //   Here we use the balance amount as the concurrency token so the lesson runs on SQLite.
    //   The concept is identical — client reads a version token and sends it back on mutation.
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id, CancellationToken ct)
    {
        var entity = await db.BankAccounts.FindAsync([id], ct);
        if (entity is null) return NotFound(new { id });

        // Use balance as a simple but illustrative concurrency token (works on SQLite)
        var version = entity.Balance.ToString("F2");
        Response.Headers.ETag = $"\"{version}\"";

        return Ok(new
        {
            entity.Id,
            entity.AccountNumber,
            entity.OwnerName,
            entity.Balance,
            RowVersion = version,
        });
    }

    // POST /ddd/advanced/accounts/{id}/deposit
    // Client must send the RowVersion obtained from GET to detect concurrent modifications.
    [HttpPost("{id:int}/deposit")]
    public async Task<IActionResult> Deposit(
        int id,
        [FromBody] AdvancedDepositRequest req,
        CancellationToken ct)
    {
        var entity = await db.BankAccounts.FindAsync([id], ct);
        if (entity is null) return NotFound(new { id });

        // Optimistic concurrency check — compare expected balance token
        if (!string.IsNullOrEmpty(req.RowVersion))
        {
            var expectedVersion = entity.Balance.ToString("F2");
            if (req.RowVersion != expectedVersion)
                return Conflict(new { error = "Concurrent modification detected. Reload and retry." });
        }

        // Apply business rule through the aggregate
        var agg = BankAccountAggregate.Reconstruct(entity.Id, entity.AccountNumber,
            entity.OwnerName, new Money(entity.Balance, "USD"));
        agg.Deposit(new Money(req.Amount, "USD"));
        entity.Balance = agg.Balance.Amount;

        try
        {
            await uow.CommitAsync([agg], ct);
        }
        catch (DomainConcurrencyException)
        {
            return Conflict(new { error = "Concurrent modification detected. Reload and retry." });
        }

        return Ok(new { entity.Id, entity.Balance });
    }

    private static Lesson.Entities.BankAccount MapToEntity(BankAccountAggregate a) => new()
    {
        AccountNumber = a.AccountNumber,
        OwnerName     = a.OwnerName,
        Balance       = a.Balance.Amount,
        AccountType   = "Savings",
    };
}

public record OpenAdvancedRequest(string AccountNumber, string OwnerName, decimal InitialBalance);
public record AdvancedDepositRequest(decimal Amount, string? RowVersion = null);
