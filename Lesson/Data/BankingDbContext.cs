using Lesson.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lesson.Data;

/// <summary>
/// Lesson 03-A — EF Core DbContext.
///
/// DbContext is the Unit-of-Work + Repository in one:
///   - tracks changes to entities in memory
///   - translates LINQ queries to SQL
///   - persists changes with SaveChangesAsync()
///
/// Java parallel:
///   EntityManager (JPA) / EntityManagerFactory → DbContext / DbContextFactory
///   @PersistenceContext EntityManager em        → inject DbContext via constructor DI
///
/// Lifetime: registered as Scoped (one per HTTP request) — EF Core default.
/// Never inject into singletons; use IDbContextFactory instead.
/// </summary>
public class BankingDbContext(DbContextOptions<BankingDbContext> options) : DbContext(options)
{
    /// <summary>
    /// DbSet is the entry point for querying and saving BankAccount entities.
    /// Java parallel: JpaRepository&lt;BankAccount, Integer&gt; / EntityManager.find()
    /// </summary>
    public DbSet<BankAccount> BankAccounts => Set<BankAccount>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Unique constraint on AccountNumber
        // Java: @Column(unique = true)
        modelBuilder.Entity<BankAccount>()
            .HasIndex(a => a.AccountNumber)
            .IsUnique();

        // Seed data — present in every migration; useful for demos and tests.
        // Java: data.sql / @Sql on tests
        modelBuilder.Entity<BankAccount>().HasData(
            new BankAccount
            {
                Id = 1,
                AccountNumber = "ACC-0001",
                OwnerName = "Alice Dupont",
                AccountType = "Checking",
                Balance = 12_500.00m,
                IsActive = true,
                CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new BankAccount
            {
                Id = 2,
                AccountNumber = "ACC-0002",
                OwnerName = "Bob Martin",
                AccountType = "Savings",
                Balance = 45_000.00m,
                IsActive = true,
                CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            }
        );
    }
}
