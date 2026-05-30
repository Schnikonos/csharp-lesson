using Lesson.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Lesson.Data;

/// <summary>
/// Lesson 03-C — global query filter (soft delete) + SaveChangesAsync audit override.
/// Lesson 03-B — OwnsOne&lt;Address&gt; configuration.
/// Lesson 03-A — EF Core DbContext.
/// </summary>
public class BankingDbContext(DbContextOptions<BankingDbContext> options) : DbContext(options)
{
    public DbSet<BankAccount> BankAccounts => Set<BankAccount>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<BankAccount>()
            .HasIndex(a => a.AccountNumber)
            .IsUnique();

        // ── Soft delete: global query filter ─────────────────────────────────
        // Every LINQ query on BankAccounts automatically adds WHERE IsDeleted = 0.
        // Use IgnoreQueryFilters() to bypass it when you need to see deleted rows.
        // Java parallel: @Where(clause = "is_deleted = false") on the entity class.
        modelBuilder.Entity<BankAccount>()
            .HasQueryFilter(a => !a.IsDeleted);

        // Owned entity
        modelBuilder.Entity<BankAccount>()
            .OwnsOne(a => a.Address);

        // Seed data
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

    // ── Audit override ────────────────────────────────────────────────────────
    // Intercepts every save and automatically stamps CreatedAt / UpdatedAt / UpdatedBy.
    // Java parallel: @PrePersist / @PreUpdate lifecycle callbacks,
    //                or Spring Data's @CreatedDate / @LastModifiedDate / @LastModifiedBy.
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        foreach (EntityEntry<BankAccount> entry in ChangeTracker.Entries<BankAccount>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
            }

            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Entity.UpdatedAt  = now;
                // In a real app, pull the current user from IHttpContextAccessor.
                entry.Entity.UpdatedBy ??= "system";
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }
}
