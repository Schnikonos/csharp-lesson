using Lesson.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Lesson.Data;

/// <summary>
/// Lesson 04-A — adds DbSet&lt;Customer&gt; and configures the one-to-many relationship.
/// Lesson 03-C — global query filter (soft delete) + SaveChangesAsync audit override.
/// Lesson 03-B — OwnsOne&lt;Address&gt; configuration.
/// Lesson 03-A — EF Core DbContext.
/// </summary>
public class BankingDbContext(DbContextOptions<BankingDbContext> options) : DbContext(options)
{
    public DbSet<BankAccount> BankAccounts => Set<BankAccount>();

    /// <summary>
    /// DbSet for the Customer aggregate (the "one" side of Customer → BankAccounts).
    /// Java parallel: JpaRepository&lt;Customer, Integer&gt;
    /// </summary>
    public DbSet<Customer> Customers => Set<Customer>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<BankAccount>()
            .HasIndex(a => a.AccountNumber)
            .IsUnique();

        // ── Soft delete: global query filter ─────────────────────────────────
        modelBuilder.Entity<BankAccount>()
            .HasQueryFilter(a => !a.IsDeleted);

        // ── One-to-many: Customer → BankAccounts ─────────────────────────────
        // HasMany / WithOne configures the relationship.
        // HasForeignKey names the FK column; IsRequired(false) makes it nullable
        // so an account can exist without a customer (backwards-compatible with seed data).
        //
        // Java parallel:
        //   @OneToMany(mappedBy = "customer") on Customer.accounts
        //   @ManyToOne @JoinColumn(name = "customer_id") on BankAccount.customer
        modelBuilder.Entity<Customer>()
            .HasMany(c => c.Accounts)
            .WithOne(a => a.Customer)
            .HasForeignKey(a => a.CustomerId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);

        // Owned entity
        modelBuilder.Entity<BankAccount>()
            .OwnsOne(a => a.Address);

        // Seed customers
        modelBuilder.Entity<Customer>().HasData(
            new Customer { Id = 1, Name = "Alice Dupont", Email = "alice@example.com" },
            new Customer { Id = 2, Name = "Bob Martin",   Email = "bob@example.com" }
        );

        // Seed accounts (linked to customers)
        modelBuilder.Entity<BankAccount>().HasData(
            new BankAccount
            {
                Id = 1,
                AccountNumber = "ACC-0001",
                OwnerName = "Alice Dupont",
                AccountType = "Checking",
                Balance = 12_500.00m,
                IsActive = true,
                CustomerId = 1,
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
                CustomerId = 2,
                CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            }
        );
    }

    // ── Audit override ────────────────────────────────────────────────────────
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        foreach (EntityEntry<BankAccount> entry in ChangeTracker.Entries<BankAccount>())
        {
            if (entry.State == EntityState.Added)
                entry.Entity.CreatedAt = now;

            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Entity.UpdatedAt  = now;
                entry.Entity.UpdatedBy ??= "system";
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }
}
