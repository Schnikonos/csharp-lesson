using MediatR;

namespace Lesson.Features.Accounts.DomainEvents;

/// <summary>
/// Lesson 18-C — INotification domain event.
///
/// INotification is MediatR's pub/sub event type.
/// Unlike IRequest (which has exactly one handler), INotification
/// can have MULTIPLE notification handlers — each runs independently.
///
/// Domain events are raised INSIDE the aggregate (or command handler)
/// and dispatched AFTER the transaction commits so they don't violate
/// atomicity within the write model.
///
/// Java parallel:
///   Spring @DomainEvents + @AfterDomainEventPublication  →  INotification + INotificationHandler
///   Axon @EventSourcingHandler                          →  INotificationHandler
/// </summary>
public record AccountCreatedDomainEvent(
    int     AccountId,
    string  AccountNumber,
    string  OwnerName,
    decimal InitialBalance) : INotification;
