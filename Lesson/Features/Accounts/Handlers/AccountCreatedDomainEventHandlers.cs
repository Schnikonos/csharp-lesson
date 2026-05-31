using Lesson.Features.Accounts.DomainEvents;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Lesson.Features.Accounts.Handlers;

/// <summary>
/// Lesson 18-C — Domain event notification handler: audit log.
///
/// Multiple INotificationHandler&lt;T&gt; can subscribe to the same domain event.
/// MediatR calls all of them after the event is published.
///
/// This handler simply logs the creation — in production it would write to an
/// audit log, send a welcome email, or publish an integration event to the bus.
/// </summary>
public class AccountCreatedAuditHandler(ILogger<AccountCreatedAuditHandler> logger)
    : INotificationHandler<AccountCreatedDomainEvent>
{
    public Task Handle(AccountCreatedDomainEvent notification, CancellationToken ct)
    {
        logger.LogInformation(
            "[DomainEvent:Audit] Account {Id} ({Number}) created for {Owner}",
            notification.AccountId, notification.AccountNumber, notification.OwnerName);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Second handler — demonstrates multiple subscribers to the same domain event.
/// Java parallel: multiple @EventListener beans for the same ApplicationEvent type.
/// </summary>
public class AccountCreatedWelcomeHandler(ILogger<AccountCreatedWelcomeHandler> logger)
    : INotificationHandler<AccountCreatedDomainEvent>
{
    public Task Handle(AccountCreatedDomainEvent notification, CancellationToken ct)
    {
        logger.LogInformation(
            "[DomainEvent:Welcome] Sending welcome message to owner of account {Number}",
            notification.AccountNumber);
        return Task.CompletedTask;
    }
}
