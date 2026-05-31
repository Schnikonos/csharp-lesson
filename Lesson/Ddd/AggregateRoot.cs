using MediatR;

namespace Lesson.Ddd;

/// <summary>
/// Lesson 19-A — Aggregate root base class.
///
/// An aggregate root is the single entry point into an aggregate (cluster of entities).
/// It owns domain events and enforces all invariants within the boundary.
///
/// Key rules:
///   1. External code only holds references to the aggregate root, not to inner entities.
///   2. The root raises domain events; the application layer dispatches them post-commit.
///   3. Identities are strongly typed to prevent mixing IDs across aggregates.
///
/// Java parallel:
///   Axon @Aggregate              →  AggregateRoot base class
///   @AggregateIdentifier field   →  public Guid Id { get; }
///   @DomainEvents list           →  private List&lt;INotification&gt; _domainEvents
/// </summary>
public abstract class AggregateRoot
{
    private readonly List<INotification> _domainEvents = [];

    /// <summary>Enqueues a domain event to be dispatched post-commit.</summary>
    protected void RaiseDomainEvent(INotification domainEvent) =>
        _domainEvents.Add(domainEvent);

    /// <summary>Dequeues and returns all pending domain events. Called by the infrastructure layer.</summary>
    public IReadOnlyList<INotification> PopDomainEvents()
    {
        var events = _domainEvents.ToList();
        _domainEvents.Clear();
        return events;
    }
}
