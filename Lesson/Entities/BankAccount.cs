using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Lesson.Entities;

/// <summary>
/// Lesson 04-A — adds CustomerId FK and Customer navigation property (many-to-one side).
/// Lesson 03-C — adds RowVersion (optimistic concurrency), IsDeleted (soft delete),
///               UpdatedAt / UpdatedBy (audit fields set by SaveChangesAsync override).
/// Lesson 03-B — adds owned Address value object.
/// Lesson 03-A — EF Core entity (code-first).
///
/// Java parallel:
///   @Entity + @Table              → class + [Table] attribute
///   @Id + @GeneratedValue         → [Key] + [DatabaseGenerated(Identity)]
///   @ManyToOne @JoinColumn        → CustomerId (FK) + Customer? (navigation)
///   @Version byte[]               → [Timestamp] / [ConcurrencyCheck]
///   @SQLDelete / @Where           → global query filter (HasQueryFilter)
///   @PrePersist / @PreUpdate      → SaveChangesAsync override in DbContext
/// </summary>
[Table("BankAccounts")]
public class BankAccount
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required][MaxLength(50)]
    public string AccountNumber { get; set; } = string.Empty;

    [Required][MaxLength(100)]
    public string OwnerName { get; set; } = string.Empty;

    [Required][MaxLength(20)]
    public string AccountType { get; set; } = "Checking";

    [Column(TypeName = "decimal(18,2)")]
    public decimal Balance { get; set; }

    public bool IsActive { get; set; } = true;

    // ── Audit fields ─────────────────────────────────────────────────────────
    // CreatedAt is set once on insert by SaveChangesAsync override.
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // UpdatedAt / UpdatedBy are set on every update by SaveChangesAsync override.
    // Java parallel: @PreUpdate / @LastModifiedDate / @LastModifiedBy
    public DateTime? UpdatedAt { get; set; }

    [MaxLength(100)]
    public string? UpdatedBy { get; set; }

    // ── Soft delete ───────────────────────────────────────────────────────────
    // Rows with IsDeleted = true are hidden by a global query filter in DbContext.
    // Java parallel: @SQLDelete(sql = "UPDATE bank_accounts SET is_deleted = true WHERE id = ?")
    //                @Where(clause = "is_deleted = false")
    public bool IsDeleted { get; set; } = false;

    // ── Optimistic concurrency ────────────────────────────────────────────────
    // EF Core automatically adds this to every UPDATE/DELETE WHERE clause.
    // If another thread changed the row, the WHERE finds no row → DbUpdateConcurrencyException.
    // Java parallel: @Version private byte[] rowVersion;
    [Timestamp]
    public byte[]? RowVersion { get; set; }

    // ── Owned value object ────────────────────────────────────────────────────
    public Address? Address { get; set; }

    // ── Many-to-one navigation ────────────────────────────────────────────────
    // CustomerId is the shadow FK column in the database.
    // Customer is the in-memory navigation — populated only when you call Include().
    // Java parallel: @ManyToOne @JoinColumn(name = "customer_id") private Customer customer;
    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }

    // ── One-to-many navigation ────────────────────────────────────────────────
    // Lesson 04-B — an account can have many transactions.
    // Java parallel: @OneToMany(mappedBy = "bankAccount") private List<Transaction> transactions;
    public ICollection<Transaction> Transactions { get; set; } = [];
}
