namespace Lesson.Entities;

/// <summary>
/// Owned value object stored inside the BankAccounts table (no separate table / no FK).
///
/// EF Core concept: [Owned] entities share the primary key of their owner.
/// Java parallel: @Embeddable / @Embedded in JPA.
/// </summary>
public class Address
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
}
