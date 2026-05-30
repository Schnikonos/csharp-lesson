using Lesson.Data;
using Lesson.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lesson.Repositories;

/// <summary>
/// Lesson 03-B — concrete repository backed by BankingDbContext.
///
/// KEY LESSON: IQueryable&lt;T&gt; vs IEnumerable&lt;T&gt;
/// ─────────────────────────────────────────────
/// IQueryable&lt;T&gt; represents an *unevaluated* query expression tree.
/// Additional LINQ operators (.Where, .OrderBy, .Select …) are composed into
/// a single SQL statement that is sent to the database only when you enumerate
/// it (ToListAsync, FirstOrDefaultAsync, etc.).
///
/// IEnumerable&lt;T&gt; represents in-memory data.
/// Calling AsEnumerable() (or ToList()) materialises the rows from the DB
/// *immediately*. Any subsequent LINQ operations run in C# on all rows that
/// were fetched, potentially loading far more data than needed.
///
/// Rule of thumb: keep the query as IQueryable until you actually need the data.
/// </summary>
public class AccountRepository(BankingDbContext db) : IAccountRepository
{
    public async Task<IReadOnlyList<BankAccount>> GetAllAsync(string? accountType = null)
    {
        // Start with the full table — no SQL sent yet.
        IQueryable<BankAccount> query = db.BankAccounts;

        // ✅ IQueryable composition: adds a WHERE clause to the SQL.
        //    The database filters the rows; only matching rows travel over the wire.
        if (!string.IsNullOrWhiteSpace(accountType))
            query = query.Where(a => a.AccountType == accountType);

        // ❌ IEnumerable anti-pattern (for illustration — NOT used here):
        //    IEnumerable<BankAccount> all = await db.BankAccounts.ToListAsync();
        //    var filtered = all.Where(a => a.AccountType == accountType);
        //    → Fetches ALL rows from DB first, then filters in C# memory.

        // ToListAsync() sends the final SQL and materialises the result.
        return await query
            .OrderBy(a => a.AccountNumber)
            .ToListAsync();
    }

    public async Task<BankAccount?> GetByIdAsync(int id)
    {
        // FindAsync checks the DbContext identity cache before hitting the DB.
        // Java parallel: EntityManager.find(BankAccount.class, id)
        return await db.BankAccounts.FindAsync(id);
    }

    public async Task<bool> ExistsAsync(string accountNumber)
    {
        // AnyAsync → SELECT EXISTS(...) — more efficient than fetching the full entity.
        return await db.BankAccounts.AnyAsync(a => a.AccountNumber == accountNumber);
    }

    public async Task<BankAccount> AddAsync(BankAccount account)
    {
        db.BankAccounts.Add(account);
        await db.SaveChangesAsync();
        return account; // Id is now populated by EF Core
    }

    public async Task UpdateAsync(BankAccount account)
    {
        // The entity was already loaded via FindAsync so it is tracked by the DbContext.
        // EF Core detects which properties changed and issues an UPDATE for those columns only.
        // No explicit db.Update() call is needed.
        await db.SaveChangesAsync();
    }

    public async Task DeleteAsync(BankAccount account)
    {
        db.BankAccounts.Remove(account);
        await db.SaveChangesAsync();
    }
}
