using Lesson.Controllers;
using Lesson.Data;
using Lesson.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lesson.Repositories;

/// <summary>
/// Lesson 04-B — adds pagination (Skip/Take), projection (Select into DTO),
///               GroupBy aggregates, Any/All/Count predicates.
/// Lesson 03-C — soft delete, restore, GetDeletedAsync; SaveChangesAsync calls
///               moved to IUnitOfWork.CommitAsync() so the Unit of Work controls
///               the transaction boundary.
/// Lesson 03-B — concrete repository backed by BankingDbContext.
///
/// KEY LESSON: IQueryable vs IEnumerable
/// IQueryable = unevaluated query expression tree — SQL is composed before being sent.
/// IEnumerable = in-memory data — fetches ALL rows, then filters in C#.
/// Rule: keep queries as IQueryable until you actually need the data (ToListAsync).
/// </summary>
public class AccountRepository(BankingDbContext db) : IAccountRepository
{
    // ── Lesson 03-B / 03-C methods ────────────────────────────────────────────

    public async Task<IReadOnlyList<BankAccount>> GetAllAsync(string? accountType = null)
    {
        IQueryable<BankAccount> query = db.BankAccounts; // global filter: IsDeleted = false
        if (!string.IsNullOrWhiteSpace(accountType))
            query = query.Where(a => a.AccountType == accountType);
        return await query.OrderBy(a => a.AccountNumber).ToListAsync();
    }

    /// <summary>
    /// IgnoreQueryFilters() bypasses all global filters on BankAccounts,
    /// exposing soft-deleted rows.
    /// Java parallel: @Query("SELECT a FROM BankAccount a WHERE a.isDeleted = true")
    /// </summary>
    public async Task<IReadOnlyList<BankAccount>> GetDeletedAsync()
        => await db.BankAccounts
               .IgnoreQueryFilters()
               .Where(a => a.IsDeleted)
               .OrderBy(a => a.AccountNumber)
               .ToListAsync();

    public async Task<BankAccount?> GetByIdAsync(int id)
        => await db.BankAccounts.FindAsync(id);

    public async Task<bool> ExistsAsync(string accountNumber)
        => await db.BankAccounts.AnyAsync(a => a.AccountNumber == accountNumber);

    public async Task<BankAccount> AddAsync(BankAccount account)
    {
        db.BankAccounts.Add(account);
        // NOTE: do NOT call SaveChangesAsync here — caller uses UoW.CommitAsync().
        return account;
    }

    public Task UpdateAsync(BankAccount account) => Task.CompletedTask;

    /// <summary>
    /// Soft delete — marks the row as deleted without issuing a SQL DELETE.
    /// Java parallel: @SQLDelete(sql = "UPDATE bank_accounts SET is_deleted = true WHERE id = ?")
    /// </summary>
    public Task SoftDeleteAsync(BankAccount account)
    {
        account.IsDeleted = true;
        return Task.CompletedTask;
    }

    public Task RestoreAsync(BankAccount account)
    {
        account.IsDeleted = false;
        return Task.CompletedTask;
    }

    // ── Lesson 04-B ───────────────────────────────────────────────────────────

    /// <summary>
    /// Pagination via Skip / Take.
    /// Select() projects into a lightweight DTO — only the required columns travel over the wire.
    /// Java parallel: repository.findAll(PageRequest.of(page, pageSize)) returning Page&lt;T&gt;.
    /// </summary>
    public async Task<PagedResult<AccountSummaryDto>> GetPagedSummariesAsync(int page, int pageSize)
    {
        IQueryable<BankAccount> baseQuery = db.BankAccounts.OrderBy(a => a.AccountNumber);

        int total = await baseQuery.CountAsync();

        // Skip / Take translate to SQL OFFSET / LIMIT
        List<AccountSummaryDto> items = await baseQuery
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            // Select — projection; EF Core only SELECTs the named columns
            .Select(a => new AccountSummaryDto(
                a.Id, a.AccountNumber, a.OwnerName, a.AccountType, a.Balance, a.IsActive))
            .ToListAsync();

        return new PagedResult<AccountSummaryDto>(items, total, page, pageSize);
    }

    /// <summary>
    /// GroupBy aggregate — GROUP BY AccountType with COUNT / SUM / AVG.
    ///
    /// NOTE: SQLite's decimal support is limited, so we project to a minimal anonymous
    /// type server-side (SELECT AccountType, Balance) and then group in C# memory.
    /// On SQL Server or PostgreSQL this translates fully to SQL GROUP BY.
    ///
    /// Java parallel: @Query("SELECT a.accountType, COUNT(a), SUM(a.balance), AVG(a.balance) FROM ...")
    /// </summary>
    public async Task<IReadOnlyList<AccountTypeStatDto>> GetStatsByTypeAsync()
    {
        // IQueryable — only AccountType and Balance columns are SELECTed from the DB
        var rows = await db.BankAccounts
            .Select(a => new { a.AccountType, a.Balance })
            .ToListAsync();

        // IEnumerable GroupBy runs in C# — safe with decimal on any provider
        return rows
            .GroupBy(a => a.AccountType)
            .Select(g => new AccountTypeStatDto(
                g.Key,
                g.Count(),
                g.Sum(a => a.Balance),
                (double)g.Average(a => a.Balance)))
            .OrderBy(s => s.AccountType)
            .ToList();
    }

    /// <summary>
    /// Any() — translates to SQL EXISTS (no rows fetched).
    /// Java parallel: repository.existsByBalanceGreaterThan(threshold)
    /// </summary>
    public Task<bool> AnyWithBalanceAboveAsync(decimal threshold)
        => db.BankAccounts.AnyAsync(a => a.Balance > threshold);

    /// <summary>
    /// All() — translates to NOT EXISTS (... WHERE NOT condition).
    /// Java parallel: no direct Spring Data method; typically a custom @Query.
    /// </summary>
    public Task<bool> AllPositiveBalanceAsync()
        => db.BankAccounts.AllAsync(a => a.Balance > 0);

    /// <summary>
    /// Count() — translates to SQL COUNT(*) with an optional WHERE clause.
    /// Java parallel: repository.countByAccountType(type)
    /// </summary>
    public Task<int> CountActiveAsync(string? accountType = null)
    {
        IQueryable<BankAccount> query = db.BankAccounts.Where(a => a.IsActive);
        if (!string.IsNullOrWhiteSpace(accountType))
            query = query.Where(a => a.AccountType == accountType);
        return query.CountAsync();
    }
}
