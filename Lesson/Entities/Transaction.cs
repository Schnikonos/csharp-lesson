using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Lesson.Entities;

/// <summary>
/// Lesson 04-B — new entity representing a debit or credit transaction on a BankAccount.
/// Demonstrates GroupBy aggregates (sum/avg per account) and ThenInclude two levels deep.
///
/// Java parallel:
///   @Entity @Table(name = "Transactions")
///   @ManyToOne @JoinColumn(name = "bank_account_id") private BankAccount account;
/// </summary>
[Table("Transactions")]
public class Transaction
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    // FK to BankAccount
    public int BankAccountId { get; set; }
    public BankAccount BankAccount { get; set; } = null!;

    /// <summary>"Credit" or "Debit"</summary>
    [Required][MaxLength(10)]
    public string Type { get; set; } = "Credit";

    [Column(TypeName = "decimal(18,2)")]
    public decimal Amount { get; set; }

    [MaxLength(200)]
    public string? Description { get; set; }

    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
}
