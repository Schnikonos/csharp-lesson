using Lesson.Controllers;
using Lesson.Entities;

namespace Lesson.Repositories;

/// <summary>
/// Lesson 04-B — adds pagination, projection, aggregate, and predicate query methods.
/// Lesson 03-C — adds GetDeletedAsync / SoftDeleteAsync / RestoreAsync.
/// Lesson 03-B — Repository pattern.
///
/// Java parallel: JpaRepository&lt;BankAccount, Integer&gt; / a custom @Repository interface.
/// </summary>
public interface IAccountRepository
{
    /// <summary>Returns all non-deleted accounts, optionally filtered by account type.</summary>
    Task<IReadOnlyList<BankAccount>> GetAllAsync(string? accountType = null);

    /// <summary>
    /// Returns soft-deleted accounts (bypasses the global query filter).
    /// Java parallel: repository method that ignores the @Where clause via a native query.
    /// </summary>
    Task<IReadOnlyList<BankAccount>> GetDeletedAsync();

    /// <summary>Returns a single non-deleted account by PK, or null if not found.</summary>
    Task<BankAccount?> GetByIdAsync(int id);

    /// <summary>Returns true when an account with the given number already exists.</summary>
    Task<bool> ExistsAsync(string accountNumber);

    /// <summary>Persists a new account and returns it with its generated Id.</summary>
    Task<BankAccount> AddAsync(BankAccount account);

    /// <summary>Persists mutations made to a tracked entity.</summary>
    Task UpdateAsync(BankAccount account);

    /// <summary>
    /// Soft-deletes an account (sets IsDeleted = true) instead of issuing a DELETE statement.
    /// The row stays in the database but is hidden by the global query filter.
    /// </summary>
    Task SoftDeleteAsync(BankAccount account);

    /// <summary>
    /// Restores a soft-deleted account (sets IsDeleted = false).
    /// Requires bypassing the global query filter to find the row first.
    /// </summary>
    Task RestoreAsync(BankAccount account);

    // ── Lesson 04-B ───────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a paginated page of accounts as lightweight summary projections.
    /// Uses Select() to build the DTO in SQL — no full entity loaded.
    /// Java parallel: Pageable + Page&lt;T&gt; returned by a JPA repository.
    /// </summary>
    Task<PagedResult<AccountSummaryDto>> GetPagedSummariesAsync(int page, int pageSize);

    /// <summary>
    /// Returns per-account-type aggregates: count, total balance, average balance.
    /// Uses GroupBy().Select() translated to SQL GROUP BY.
    /// Java parallel: @Query("SELECT a.accountType, COUNT(a), SUM(a.balance) FROM ...")
    /// </summary>
    Task<IReadOnlyList<AccountTypeStatDto>> GetStatsByTypeAsync();

    /// <summary>Returns true if any active (non-deleted) account has a balance above the threshold.</summary>
    Task<bool> AnyWithBalanceAboveAsync(decimal threshold);

    /// <summary>Returns true when ALL active accounts have a positive balance.</summary>
    Task<bool> AllPositiveBalanceAsync();

    /// <summary>Returns the count of active accounts, optionally filtered by type.</summary>
    Task<int> CountActiveAsync(string? accountType = null);

    // ── Lesson 04-C ───────────────────────────────────────────────────────────

    /// <summary>
    /// Returns accounts whose balance exceeds a threshold using a hand-written SQL query
    /// via <c>FromSqlRaw</c>. The result is still a tracked <see cref="BankAccount"/> entity
    /// so further LINQ operators (Where, OrderBy, …) can be chained on top.
    ///
    /// Java parallel: @Query(value = "SELECT * FROM bank_accounts WHERE balance > :min", nativeQuery = true)
    /// </summary>
    Task<IReadOnlyList<BankAccount>> GetByRawSqlAsync(decimal minBalance);

    /// <summary>
    /// Simulates a stored-procedure call by executing a parameterised <c>FromSqlRaw</c>
    /// query named like an SP ("sp_GetAccountByNumber").  On SQL Server / PostgreSQL this
    /// would be <c>EXEC sp_GetAccountByNumber @number</c>.
    /// SQLite has no SP engine, so we fall back to an equivalent SELECT.
    ///
    /// Java parallel: @Procedure("sp_GetAccountByNumber") or EntityManager.createNativeQuery(…)
    /// </summary>
    Task<BankAccount?> GetByNumberStoredProcAsync(string accountNumber);

    /// <summary>
    /// Returns accounts with all their <c>Transactions</c> loaded using
    /// <c>AsSplitQuery()</c>: EF Core fires two SELECTs instead of a single JOIN,
    /// avoiding the cartesian product ("N×M row explosion") that occurs when multiple
    /// collection navigations are included in one query.
    ///
    /// Java parallel: two separate @Query calls / @EntityGraph with SUBSELECT fetch.
    /// </summary>
    Task<IReadOnlyList<BankAccount>> GetWithTransactionsSplitAsync();

    /// <summary>
    /// Uses a <em>compiled query</em> (<c>EF.CompileAsyncQuery</c>) to look up an account
    /// by number.  The query is translated to SQL and cached exactly once, then reused on
    /// every subsequent call — removing per-call LINQ expression-tree translation overhead.
    ///
    /// Java parallel: Hibernate named queries / @NamedNativeQuery compiled at startup.
    /// </summary>
    Task<BankAccount?> GetByNumberCompiledAsync(string accountNumber);
}
