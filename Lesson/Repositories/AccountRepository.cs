using Lesson.Data;
using Lesson.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lesson.Repositories;

/// <summary>
/// Lesson 03-C — soft delete, restore, GetDeletedAsync; SaveChangesAsync calls
///               moved to IUnitOfWork.CommitAsync() so the Unit of Work controls
///               the transaction boundary.
/// Lesson 03-B — concrete repository backed by BankingDbContext.
///
/// KEY LESSON: IQueryable&lt;T&gt; vs IEnumerable&lt;T&gt;
/// IQueryable = unevaluated query expression tree → SQL is composed before being sent.
/// IEnumerable = in-memory data → fetches ALL rows, then filters in C#.
/// Rule: keep queries as IQueryable until you actually need the data (ToListAsync).
/// </summary>
public class AccountRepository(BankingDbContext db) : IAccountRepository
{
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
        => await db.BankAccounts.FindAsync(id); // FindAsync honours global filter

    public async Task<bool> ExistsAsync(string accountNumber)
        => await db.BankAccounts.AnyAsync(a => a.AccountNumber == accountNumber);

    public async Task<BankAccount> AddAsync(BankAccount account)
    {
        db.BankAccounts.Add(account);
        // NOTE: do NOT call SaveChangesAsync here — caller uses UoW.CommitAsync().
        return account;
    }

    public Task UpdateAsync(BankAccount account)
    {
        // Entity is already tracked; EF detects changed properties automatically.
        return Task.CompletedTask;
    }

    /// <summary>
    /// Soft delete — marks the row as deleted without issuing a SQL DELETE.
    /// The global query filter then hides it from all regular queries.
    /// Java parallel: @SQLDelete(sql = "UPDATE bank_accounts SET is_deleted = true WHERE id = ?")
    /// </summary>
    public Task SoftDeleteAsync(BankAccount account)
    {
        account.IsDeleted = true;
        return Task.CompletedTask;
    }

    /// <summary>Restores a previously soft-deleted account.</summary>
    public Task RestoreAsync(BankAccount account)
    {
        account.IsDeleted = false;
        return Task.CompletedTask;
    }
}
