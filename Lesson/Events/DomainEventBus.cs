namespace Lesson.Events;

/// <summary>
/// Lesson 08-A — C# events and delegates fundamentals.
///
/// C# event system:
///   delegate      — a type-safe function pointer (can hold multiple subscribers via multicast)
///   event keyword — restricts delegate so external code can only += and -= (not invoke directly)
///   EventHandler&lt;T&gt; — the standard delegate signature: (object? sender, T args)
///
/// Java parallel:
///   EventListener interface + ApplicationEventPublisher.publishEvent()
///   OR manual observer pattern (Subject/Observer)
///
/// In this lesson all events are synchronous and in-process.
/// Lesson 08-B introduces MediatR INotification for decoupled async notification.
/// </summary>

/// <summary>
/// Event args for a payment being created.
/// Java parallel: extends ApplicationEvent or is the payload of ApplicationEvent.
/// </summary>
public class PaymentCreatedEventArgs : EventArgs
{
    public Guid PaymentId  { get; init; }
    public string FromAccount { get; init; } = string.Empty;
    public string ToAccount   { get; init; } = string.Empty;
    public decimal Amount     { get; init; }
    public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Simple in-process event bus using C# events.
///
/// The publisher raises the event; subscribers register with +=.
/// The bus is registered as a Singleton so all components share the same instance.
///
/// Java parallel:
///   @Component class that autowires ApplicationEventPublisher and calls
///   publisher.publishEvent(new PaymentCreatedEvent(this, id, ...))
/// </summary>
public class DomainEventBus
{
    /// <summary>
    /// Fired whenever a payment is created.
    /// Subscribers use:  bus.PaymentCreated += OnPaymentCreated;
    /// </summary>
    public event EventHandler<PaymentCreatedEventArgs>? PaymentCreated;

    /// <summary>
    /// Raises the PaymentCreated event.
    /// The ?. operator is null-safe — does nothing if no subscribers are registered.
    /// </summary>
    public void PublishPaymentCreated(PaymentCreatedEventArgs args) =>
        PaymentCreated?.Invoke(this, args);
}
