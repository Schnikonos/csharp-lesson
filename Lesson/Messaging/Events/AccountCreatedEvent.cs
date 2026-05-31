namespace Lesson.Messaging.Events;

/// <summary>
/// Domain event published whenever a new bank account is opened.
/// MassTransit uses the full type name as the exchange/topic name by default.
///
/// Java parallel:
///   Spring ApplicationEvent subclass  →  this record
///   @EventListener void onCreated(AccountCreatedEvent e) {}  →  IConsumer&lt;AccountCreatedEvent&gt;
/// </summary>
public record AccountCreatedEvent(
    int    AccountId,
    string AccountNumber,
    string OwnerName,
    decimal InitialBalance,
    DateTimeOffset OccurredAt);
