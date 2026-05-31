using Lesson.Domain;

namespace Lesson.Domain;

/// <summary>
/// Lesson 19-B — Repository abstraction for the BankAccountAggregate.
///
/// The repository interface lives in the domain layer.
/// The EF Core implementation lives in the infrastructure layer.
/// This inverts the dependency — the domain does NOT know about EF Core.
///
/// Java parallel:
///   Spring Data JpaRepository interface (extends Repository&lt;T,ID&gt;)
///   Axon EventSourcingRepository  →  IAggregateRepository
/// </summary>
public interface IAggregateRepository
{
    Task<BankAccountAggregate?> FindByIdAsync(int id, CancellationToken ct = default);
    Task<BankAccountAggregate?> FindByAccountNumberAsync(string number, CancellationToken ct = default);
    Task AddAsync(BankAccountAggregate aggregate, CancellationToken ct = default);
    Task SaveAsync(BankAccountAggregate aggregate, CancellationToken ct = default);
}
