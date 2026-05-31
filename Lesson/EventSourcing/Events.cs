namespace Lesson.EventSourcing;

/// <summary>
/// Marker interface for all domain events stored in the event stream.
/// </summary>
public interface IEvent
{
    Guid AggregateId { get; }
    DateTimeOffset OccurredAt { get; }
}

public record AccountOpened(Guid AggregateId, string AccountNumber, string Owner, decimal InitialBalance, DateTimeOffset OccurredAt) : IEvent;
public record MoneyDeposited(Guid AggregateId, decimal Amount, string Description, DateTimeOffset OccurredAt) : IEvent;
public record MoneyWithdrawn(Guid AggregateId, decimal Amount, string Description, DateTimeOffset OccurredAt) : IEvent;
public record AccountClosed(Guid AggregateId, DateTimeOffset OccurredAt) : IEvent;
