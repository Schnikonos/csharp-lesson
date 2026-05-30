using Lesson.Entities;

namespace Lesson.Repositories;

/// <summary>
/// Lesson 04-A — repository for the Customer aggregate.
///
/// Demonstrates Include / ThenInclude patterns for eager loading
/// navigation properties (the "one" side of Customer → BankAccounts).
///
/// Java parallel: JpaRepository&lt;Customer, Integer&gt; with
///   @EntityGraph / JOIN FETCH queries for eager loading.
/// </summary>
public interface ICustomerRepository
{
    /// <summary>
    /// Returns all customers.
    /// Accounts collection is NOT loaded — use GetByIdWithAccountsAsync for that.
    /// </summary>
    Task<IReadOnlyList<Customer>> GetAllAsync();

    /// <summary>
    /// Returns a customer with ALL their accounts eagerly loaded via Include.
    /// Java parallel: findById with @EntityGraph or JOIN FETCH.
    /// </summary>
    Task<Customer?> GetByIdWithAccountsAsync(int id);

    /// <summary>
    /// Returns a customer with only ACTIVE accounts loaded (filtered include).
    /// EF Core translates the predicate into a SQL WHERE on the join result.
    /// Java parallel: @Query("SELECT c FROM Customer c JOIN FETCH c.accounts a WHERE a.isActive = true")
    /// </summary>
    Task<Customer?> GetByIdWithActiveAccountsAsync(int id);

    Task<Customer> AddAsync(Customer customer);

    Task<bool> ExistsAsync(string email);
}
