using Lesson.Entities;
using Lesson.UnitOfWork;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Lesson.Controllers;

/// <summary>
/// Lesson 04-C — raw SQL, stored-procedure simulation, compiled queries, split queries.
/// Lesson 04-B — adds pagination, projection, GroupBy aggregate, and Any/All/Count endpoints.
/// Lesson 03-C — uses IUnitOfWork; soft delete + restore; optimistic concurrency.
/// Lesson 03-B — IAccountRepository; IQueryable filter; owned Address.
///
/// New endpoints (04-C):
///   GET  /accounts/raw?minBalance=          — FromSqlRaw parameterised query
///   GET  /accounts/by-number-sp/{number}    — simulated stored procedure call
///   GET  /accounts/by-number-compiled/{n}   — compiled query lookup
///   GET  /accounts/with-transactions        — AsSplitQuery (cartesian explosion demo)
/// </summary>
[ApiController]
[Route("accounts")]
public class AccountsController(IUnitOfWork uow) : ControllerBase
{
    // ── GET /accounts?type={accountType} ──────────────────────────────────────
    [HttpGet]
    public async Task<ActionResult<IEnumerable<AccountResponse>>> GetAll(
        [FromQuery] string? type = null)
    {
        var accounts = await uow.Accounts.GetAllAsync(type);
        return Ok(accounts.Select(ToResponse));
    }

    // ── GET /accounts/deleted ─────────────────────────────────────────────────
    [HttpGet("deleted")]
    public async Task<ActionResult<IEnumerable<AccountResponse>>> GetDeleted()
    {
        var accounts = await uow.Accounts.GetDeletedAsync();
        return Ok(accounts.Select(ToResponse));
    }

    // ── GET /accounts/{id} ───────────────────────────────────────────────────
    [HttpGet("{id:int}")]
    public async Task<ActionResult<AccountResponse>> GetById(int id)
    {
        var account = await uow.Accounts.GetByIdAsync(id);
        if (account is null)
            return NotFound(new { Error = $"Account {id} not found." });
        return Ok(ToResponse(account));
    }

    // ── POST /accounts ───────────────────────────────────────────────────────
    [HttpPost]
    public async Task<ActionResult<AccountResponse>> Create(CreateAccountRequest request)
    {
        if (await uow.Accounts.ExistsAsync(request.AccountNumber))
            return Conflict(new { Error = $"Account number '{request.AccountNumber}' already exists." });

        var account = new BankAccount
        {
            AccountNumber = request.AccountNumber,
            OwnerName     = request.OwnerName,
            AccountType   = request.AccountType,
            Balance       = request.InitialBalance,
            IsActive      = true,
            Address       = ToAddress(request.Address)
        };

        await uow.Accounts.AddAsync(account);
        await uow.CommitAsync(); // one transaction — audit stamps set in SaveChangesAsync override

        return CreatedAtAction(nameof(GetById), new { id = account.Id }, ToResponse(account));
    }

    // ── PUT /accounts/{id} ───────────────────────────────────────────────────
    [HttpPut("{id:int}")]
    public async Task<ActionResult<AccountResponse>> Update(int id, UpdateAccountRequest request)
    {
        var account = await uow.Accounts.GetByIdAsync(id);
        if (account is null)
            return NotFound(new { Error = $"Account {id} not found." });

        account.OwnerName   = request.OwnerName;
        account.AccountType = request.AccountType;
        account.Balance     = request.Balance;
        account.IsActive    = request.IsActive;
        account.Address     = ToAddress(request.Address);

        await uow.Accounts.UpdateAsync(account);

        try
        {
            await uow.CommitAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another request committed a change between our read and our write.
            // Return 409 so the client can reload the latest version and retry.
            // Java parallel: catch ObjectOptimisticLockingFailureException
            return Conflict(new { Error = "The account was modified by another request. Please reload and retry." });
        }

        return Ok(ToResponse(account));
    }

    // ── DELETE /accounts/{id} (soft delete) ──────────────────────────────────
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var account = await uow.Accounts.GetByIdAsync(id);
        if (account is null)
            return NotFound(new { Error = $"Account {id} not found." });

        await uow.Accounts.SoftDeleteAsync(account);
        await uow.CommitAsync();

        return NoContent();
    }

    // ── POST /accounts/{id}/restore ──────────────────────────────────────────
    [HttpPost("{id:int}/restore")]
    public async Task<IActionResult> Restore(int id)
    {
        // Must bypass global filter to find soft-deleted rows.
        var deleted = await uow.Accounts.GetDeletedAsync();
        var account = deleted.FirstOrDefault(a => a.Id == id);
        if (account is null)
            return NotFound(new { Error = $"No soft-deleted account with id {id} found." });

        await uow.Accounts.RestoreAsync(account);
        await uow.CommitAsync();

        return Ok(ToResponse(account));
    }

    // ── Lesson 04-B endpoints ─────────────────────────────────────────────────

    /// <summary>
    /// Paginated projection: only lightweight columns are SELECTed from the DB.
    /// Uses Skip()/Take() for OFFSET/LIMIT and Select() to build the DTO in SQL.
    /// Java parallel: Page&lt;AccountSummaryDto&gt; = repo.findAll(PageRequest.of(page-1, pageSize))
    /// </summary>
    [HttpGet("summary")]
    public async Task<ActionResult<PagedResult<AccountSummaryDto>>> GetSummaries(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 10)
    {
        if (page < 1 || pageSize < 1 || pageSize > 100)
            return BadRequest("page must be >= 1; pageSize must be between 1 and 100.");

        var result = await uow.Accounts.GetPagedSummariesAsync(page, pageSize);
        return Ok(result);
    }

    /// <summary>
    /// GroupBy aggregate: SQL GROUP BY AccountType with COUNT / SUM / AVG.
    /// Java parallel: @Query("SELECT a.accountType, COUNT(a), SUM(a.balance) FROM ...")
    /// </summary>
    [HttpGet("stats")]
    public async Task<ActionResult<IEnumerable<AccountTypeStatDto>>> GetStats()
    {
        var stats = await uow.Accounts.GetStatsByTypeAsync();
        return Ok(stats);
    }

    /// <summary>
    /// Any() — returns HTTP 200 with true/false.
    /// Translates to SQL EXISTS — no rows are fetched.
    /// </summary>
    [HttpGet("any-high-balance")]
    public async Task<ActionResult<bool>> AnyHighBalance([FromQuery] decimal threshold = 10_000m)
        => Ok(await uow.Accounts.AnyWithBalanceAboveAsync(threshold));

    /// <summary>
    /// All() — translates to NOT EXISTS (... WHERE NOT condition).
    /// Returns true only when every active account has Balance > 0.
    /// </summary>
    [HttpGet("all-positive")]
    public async Task<ActionResult<bool>> AllPositive()
        => Ok(await uow.Accounts.AllPositiveBalanceAsync());

    /// <summary>
    /// Count() — SQL COUNT(*) with optional WHERE type filter.
    /// Java parallel: repo.countByAccountType(type) or countByIsDeletedFalse()
    /// </summary>
    [HttpGet("count")]
    public async Task<ActionResult<int>> CountActive([FromQuery] string? type = null)
        => Ok(await uow.Accounts.CountActiveAsync(type));

    // ── Lesson 04-C endpoints ─────────────────────────────────────────────────

    /// <summary>
    /// FromSqlRaw — executes hand-written parameterised SQL.
    /// EF Core still returns tracked entities, so LINQ can be chained on top.
    /// </summary>
    [HttpGet("raw")]
    public async Task<ActionResult<IEnumerable<AccountResponse>>> GetByRawSql(
        [FromQuery] decimal minBalance = 0)
    {
        var accounts = await uow.Accounts.GetByRawSqlAsync(minBalance);
        return Ok(accounts.Select(ToResponse));
    }

    /// <summary>
    /// Stored-procedure simulation via FromSqlRaw.
    /// On SQL Server: EXEC sp_GetAccountByNumber {0}.
    /// </summary>
    [HttpGet("by-number-sp/{accountNumber}")]
    public async Task<ActionResult<AccountResponse>> GetByNumberStoredProc(string accountNumber)
    {
        var account = await uow.Accounts.GetByNumberStoredProcAsync(accountNumber);
        if (account is null)
            return NotFound(new { Error = $"Account '{accountNumber}' not found." });
        return Ok(ToResponse(account));
    }

    /// <summary>
    /// Compiled query — expression tree translated to SQL once, cached forever.
    /// Useful for hot paths called thousands of times per second.
    /// </summary>
    [HttpGet("by-number-compiled/{accountNumber}")]
    public async Task<ActionResult<AccountResponse>> GetByNumberCompiled(string accountNumber)
    {
        var account = await uow.Accounts.GetByNumberCompiledAsync(accountNumber);
        if (account is null)
            return NotFound(new { Error = $"Account '{accountNumber}' not found." });
        return Ok(ToResponse(account));
    }

    /// <summary>
    /// AsSplitQuery — loads accounts + their transactions via two SQL SELECTs
    /// instead of a single JOIN, avoiding the Cartesian explosion:
    ///   Without split: 2 accounts × 5 transactions = 10 result rows
    ///   With split:    2 rows + 5 rows = 7 rows (two round trips)
    /// </summary>
    [HttpGet("with-transactions")]
    public async Task<ActionResult> GetWithTransactions()
    {
        var accounts = await uow.Accounts.GetWithTransactionsSplitAsync();
        var result = accounts.Select(a => new
        {
            a.Id,
            a.AccountNumber,
            a.OwnerName,
            a.Balance,
            Transactions = a.Transactions.Select(t => new TransactionSummaryDto(
                t.Id, t.Type, t.Amount, t.Description, t.OccurredAt))
        });
        return Ok(result);
    }

    // ── Mapping helpers ───────────────────────────────────────────────────────
    private static AccountResponse ToResponse(BankAccount a) => new(
        a.Id, a.AccountNumber, a.OwnerName, a.AccountType, a.Balance, a.IsActive,
        a.CreatedAt, a.UpdatedAt, a.UpdatedBy,
        a.Address is null ? null : new AddressDto(
            a.Address.Street, a.Address.City, a.Address.PostalCode, a.Address.Country));

    private static Entities.Address? ToAddress(AddressDto? dto) =>
        dto is null ? null : new Entities.Address
        {
            Street     = dto.Street,
            City       = dto.City,
            PostalCode = dto.PostalCode,
            Country    = dto.Country
        };
}
