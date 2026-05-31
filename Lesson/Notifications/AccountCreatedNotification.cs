using MediatR;

namespace Lesson.Notifications;

/// <summary>
/// Lesson 08-B — MediatR INotification (pub/sub without direct coupling).
///
/// INotification is MediatR's "event" abstraction:
///   • The publisher calls IMediator.Publish(notification)
///   • Zero or more INotificationHandler&lt;T&gt; receive the notification
///   • Publisher has no knowledge of any handler — fully decoupled
///
/// Java parallel:
///   Spring ApplicationEvent + @EventListener — the publisher calls
///   applicationEventPublisher.publishEvent(new AccountCreatedEvent(this, id))
///   and any @EventListener method on any @Component receives it.
/// </summary>
public record AccountCreatedNotification(
    Guid AccountId,
    string OwnerName,
    decimal InitialBalance) : INotification;
