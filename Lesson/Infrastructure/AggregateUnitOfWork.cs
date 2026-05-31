using Lesson.Data;
using Lesson.Ddd;
using Lesson.Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Lesson.Infrastructure;

/// <summary>
/// Lesson 19-C — Transactional outbox pattern: dispatch domain events AFTER the DB commit.
///
/// The IUnitOfWork controls the transaction boundary. Domain events are held in
/// aggregate roots and flushed here, AFTER SaveChangesAsync succeeds.
/// This guarantees that events are only dispatched when the write is durable.
///
/// If SaveChanges throws DbUpdateConcurrencyException (optimistic lock conflict),
/// events are never dispatched — the operation is retried by the caller.
///
/// Java parallel:
///   Spring @TransactionalEventListener(phase = AFTER_COMMIT)
///   Axon @AfterDomainEventPublication
/// </summary>
public class AggregateUnitOfWork(BankingDbContext db, IPublisher publisher)
{
    /// <summary>
    /// Save all pending EF Core changes and dispatch any domain events from
    /// the supplied aggregates. Throws <see cref="DomainConcurrencyException"/>
    /// if a concurrency conflict is detected.
    /// </summary>
    public async Task CommitAsync(IEnumerable<AggregateRoot> aggregates, CancellationToken ct = default)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // Translate EF Core exception into a domain exception the caller can handle
            throw new DomainConcurrencyException(
                "A concurrency conflict was detected. Reload and retry.", ex);
        }

        // Dispatch domain events only after the write is committed
        foreach (var aggregate in aggregates)
            foreach (var domainEvent in aggregate.PopDomainEvents())
                await publisher.Publish(domainEvent, ct);
    }
}

/// <summary>
/// Lesson 19-C — Domain exception for optimistic concurrency conflicts.
/// Wraps <see cref="DbUpdateConcurrencyException"/> so the domain layer stays
/// free of EF Core references.
///
/// Java parallel: JPA OptimisticLockException / Spring's
/// ObjectOptimisticLockingFailureException
/// </summary>
public sealed class DomainConcurrencyException(string message, Exception inner)
    : Exception(message, inner);
