using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Lesson.Entities;

/// <summary>
/// Lesson 04-A — the "one" side of a one-to-many relationship.
///
/// A Customer can have zero or more BankAccounts.
/// EF Core tracks this via a navigation property (Accounts) on this class
/// and a foreign key (CustomerId) on BankAccount.
///
/// Java parallel:
///   @Entity Customer with @OneToMany(mappedBy = "customer") List&lt;BankAccount&gt; accounts.
///   Navigation property (C#) ≈ the Java collection field annotated @OneToMany.
/// </summary>
[Table("Customers")]
public class Customer
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required][MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required][MaxLength(200)]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    // ── Navigation property ───────────────────────────────────────────────────
    // EF Core populates this collection when you call .Include(c => c.Accounts).
    // Without Include, it stays empty (no lazy loading by default).
    // Java parallel: @OneToMany(mappedBy = "customer", fetch = FetchType.LAZY)
    public ICollection<BankAccount> Accounts { get; set; } = [];
}
