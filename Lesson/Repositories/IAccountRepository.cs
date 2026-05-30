using Lesson.Entities;

namespace Lesson.Repositories;

/// <summary>
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
}
