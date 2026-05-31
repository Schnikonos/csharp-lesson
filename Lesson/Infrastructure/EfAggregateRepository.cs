using Lesson.Data;
using Lesson.Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Lesson.Infrastructure;

/// <summary>
/// Lesson 19-B — EF Core implementation of IAggregateRepository.
///
/// This adapter maps between BankAccountAggregate (domain) and
/// BankAccount (EF Core entity). In a real project you might use
/// EF Core directly on the aggregate; here we keep them separate
/// to show the anti-corruption layer concept clearly.
///
/// Java parallel:
///   Spring Data @Repository implementing a custom repository interface
///   EF Core DbContext ≡ EntityManager / JPA persistence context
/// </summary>
public class EfAggregateRepository(BankingDbContext db, IPublisher publisher) : IAggregateRepository
{
    public async Task<BankAccountAggregate?> FindByIdAsync(int id, CancellationToken ct = default)
    {
        var entity = await db.BankAccounts.FirstOrDefaultAsync(a => a.Id == id, ct);
        return entity is null ? null : MapToDomain(entity);
    }

    public async Task<BankAccountAggregate?> FindByAccountNumberAsync(string number, CancellationToken ct = default)
    {
        var entity = await db.BankAccounts.FirstOrDefaultAsync(a => a.AccountNumber == number, ct);
        return entity is null ? null : MapToDomain(entity);
    }

    public async Task AddAsync(BankAccountAggregate aggregate, CancellationToken ct = default)
    {
        var entity = MapToEntity(aggregate);
        db.BankAccounts.Add(entity);
        await db.SaveChangesAsync(ct);

        // Copy EF-assigned id back to aggregate (simplified — real impl uses EF tracking)
        SetId(aggregate, entity.Id);
        await DispatchDomainEvents(aggregate);
    }

    public async Task SaveAsync(BankAccountAggregate aggregate, CancellationToken ct = default)
    {
        var entity = await db.BankAccounts.FindAsync([GetId(aggregate)], ct);
        if (entity is null) throw new InvalidOperationException("Aggregate not found in DB");

        entity.Balance = aggregate.Balance.Amount;
        await db.SaveChangesAsync(ct);
        await DispatchDomainEvents(aggregate);
    }

    private static BankAccountAggregate MapToDomain(Lesson.Entities.BankAccount e) =>
        BankAccountAggregate.Reconstruct(e.Id, e.AccountNumber, e.OwnerName, new Money(e.Balance, "USD"));

    private static Lesson.Entities.BankAccount MapToEntity(BankAccountAggregate a) => new()
    {
        AccountNumber = a.AccountNumber,
        OwnerName     = a.OwnerName,
        Balance       = a.Balance.Amount,
        AccountType   = "Savings",
    };

    private async Task DispatchDomainEvents(BankAccountAggregate aggregate)
    {
        foreach (var ev in aggregate.PopDomainEvents())
            await publisher.Publish(ev);
    }

    private static void SetId(BankAccountAggregate agg, int id) => agg.Id = id;
    private static int GetId(BankAccountAggregate agg) => agg.Id;
}
