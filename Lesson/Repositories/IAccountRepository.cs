using Lesson.Entities;

namespace Lesson.Repositories;

/// <summary>
/// Lesson 03-B — Repository pattern.
///
/// The repository abstracts data-access details from the controller.
/// The controller depends on this interface, not on DbContext directly,
/// which makes unit-testing the controller possible without a database.
///
/// Java parallel: JpaRepository&lt;BankAccount, Integer&gt; / a custom @Repository interface.
/// </summary>
public interface IAccountRepository
{
    /// <summary>
    /// Returns all accounts, optionally filtered by account type.
    ///
    /// IQueryable&lt;T&gt; key point: the filter is added BEFORE the SQL is sent —
    /// a WHERE clause is generated, not an in-memory loop.
    /// </summary>
    Task<IReadOnlyList<BankAccount>> GetAllAsync(string? accountType = null);

    /// <summary>Returns a single account by PK, or null if not found.</summary>
    Task<BankAccount?> GetByIdAsync(int id);

    /// <summary>Returns true when an account with the given number already exists.</summary>
    Task<bool> ExistsAsync(string accountNumber);

    /// <summary>Persists a new account and returns it with its generated Id.</summary>
    Task<BankAccount> AddAsync(BankAccount account);

    /// <summary>Persists mutations made to a tracked entity.</summary>
    Task UpdateAsync(BankAccount account);

    /// <summary>Removes an account from the database.</summary>
    Task DeleteAsync(BankAccount account);
}
