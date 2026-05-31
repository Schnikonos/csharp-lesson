using Lesson.Events;

namespace Lesson.Subscribers;

/// <summary>
/// Lesson 08-A — Event subscriber (observer).
///
/// Subscribes to DomainEventBus.PaymentCreated in the constructor and
/// maintains an in-memory audit log of all payments seen since startup.
///
/// Registered as Singleton so the log persists across requests.
///
/// Java parallel:
///   @Component class with @EventListener(PaymentCreatedEvent.class) method.
///   Spring calls the method automatically when the event is published.
/// </summary>
public class PaymentAuditSubscriber
{
    private readonly List<PaymentCreatedEventArgs> _log = [];

    public PaymentAuditSubscriber(DomainEventBus bus)
    {
        // Subscribe using the += operator — equivalent to registering a listener.
        // The handler signature must match EventHandler<PaymentCreatedEventArgs>:
        //   void Handler(object? sender, PaymentCreatedEventArgs args)
        bus.PaymentCreated += OnPaymentCreated;
    }

    public IReadOnlyList<PaymentCreatedEventArgs> Log => _log.AsReadOnly();

    private void OnPaymentCreated(object? sender, PaymentCreatedEventArgs args)
    {
        _log.Add(args);
    }
}
