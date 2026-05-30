using Lesson.Data;
using Lesson.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lesson.Repositories;

/// <summary>
/// Lesson 04-A — demonstrates Include, filtered Include, and AnyAsync.
///
/// KEY LESSONS
/// ───────────
/// Include(c => c.Accounts)
///   Generates a SQL JOIN (or a second query with AsSplitQuery) and populates
///   the Accounts navigation collection on each returned Customer.
///   Without Include, Accounts stays empty — EF Core does NOT lazy-load by default.
///
/// Filtered Include  .Include(c => c.Accounts.Where(a => a.IsActive))
///   Adds a WHERE clause to the include JOIN so only matching related entities
///   are fetched. Available from EF Core 5 onward.
///   Java parallel: no direct equivalent — usually done with @Query / JPQL or
///   Hibernate @Filter on the collection.
///
/// ThenInclude
///   Chains deeper: .Include(c => c.Accounts).ThenInclude(a => a.Address)
///   Loads the owned Address for every Account in one round-trip.
///   Java parallel: JOIN FETCH c.accounts a JOIN FETCH a.address
/// </summary>
public class CustomerRepository(BankingDbContext db) : ICustomerRepository
{
    public async Task<IReadOnlyList<Customer>> GetAllAsync()
        => await db.Customers
               .OrderBy(c => c.Name)
               .ToListAsync();

    /// <summary>
    /// Eager-loads all accounts and their owned Address in one query.
    ///
    /// SQL sketch:
    ///   SELECT c.*, a.*, a.Address_*
    ///   FROM Customers c
    ///   LEFT JOIN BankAccounts a ON a.CustomerId = c.Id
    ///   WHERE c.Id = @id AND a.IsDeleted = 0
    ///
    /// Note: the global query filter on BankAccounts (IsDeleted = 0) is
    /// automatically applied to the Include as well.
    /// </summary>
    public async Task<Customer?> GetByIdWithAccountsAsync(int id)
        => await db.Customers
               .Include(c => c.Accounts)          // JOIN BankAccounts
                   .ThenInclude(a => a.Address)    // also load owned Address columns
               .FirstOrDefaultAsync(c => c.Id == id);

    /// <summary>
    /// Filtered Include — only active accounts are loaded into the collection.
    /// The Accounts list on the returned Customer contains only IsActive = true rows.
    /// </summary>
    public async Task<Customer?> GetByIdWithActiveAccountsAsync(int id)
        => await db.Customers
               .Include(c => c.Accounts.Where(a => a.IsActive))
               .FirstOrDefaultAsync(c => c.Id == id);

    public async Task<Customer> AddAsync(Customer customer)
    {
        db.Customers.Add(customer);
        return customer; // caller uses IUnitOfWork.CommitAsync()
    }

    public async Task<bool> ExistsAsync(string email)
        => await db.Customers.AnyAsync(c => c.Email == email);
}
