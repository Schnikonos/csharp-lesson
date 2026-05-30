using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Lesson.Entities;

/// <summary>
/// Lesson 03-A — EF Core entity (code-first).
///
/// Java parallel:
///   @Entity + @Table           → class + [Table] attribute (optional if name matches)
///   @Id + @GeneratedValue      → [Key] + [DatabaseGenerated(Identity)]
///   @Column(nullable = false)  → [Required] / [MaxLength]
///   @Enumerated(STRING)        → store enum as string via HasConversion in DbContext
/// </summary>
[Table("BankAccounts")]
public class BankAccount
{
    // EF Core convention: property named "Id" or "{TypeName}Id" is auto-detected as PK.
    // Java: @Id @GeneratedValue(strategy = GenerationType.IDENTITY)
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required]
    [MaxLength(50)]
    public string AccountNumber { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string OwnerName { get; set; } = string.Empty;

    // Store as string in SQLite for readability.
    // Java: @Enumerated(EnumType.STRING)
    [Required]
    [MaxLength(20)]
    public string AccountType { get; set; } = "Checking"; // "Checking" | "Savings"

    [Column(TypeName = "decimal(18,2)")]
    public decimal Balance { get; set; }

    public bool IsActive { get; set; } = true;

    // Audit fields — kept simple for 03-A; full audit trail in 03-C.
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
