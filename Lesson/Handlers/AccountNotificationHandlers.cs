using Lesson.Notifications;
using MediatR;

namespace Lesson.Handlers;

/// <summary>
/// Lesson 08-B — First notification handler: email notification (simulated).
///
/// Multiple handlers can listen to the same notification.
/// MediatR calls all registered handlers — in no guaranteed order by default.
///
/// Java parallel: Two @EventListener methods (on different @Components)
/// both annotated with the same event type.
/// </summary>
public class SendWelcomeEmailHandler(ILogger<SendWelcomeEmailHandler> logger)
    : INotificationHandler<AccountCreatedNotification>
{
    // INotificationHandler is registered automatically by AddMediatR(assemblyContaining<Program>)
    public Task Handle(AccountCreatedNotification notification, CancellationToken ct)
    {
        // Simulated: in production you would call an SMTP / SES / SendGrid client here
        logger.LogInformation(
            "📧 Welcome email queued for {Owner} (account {Id})",
            notification.OwnerName, notification.AccountId);

        return Task.CompletedTask;
    }
}

/// <summary>
/// Lesson 08-B — Second notification handler: audit log.
/// </summary>
public class AccountCreatedAuditHandler : INotificationHandler<AccountCreatedNotification>
{
    private static readonly List<AccountCreatedNotification> _log = [];
    public static IReadOnlyList<AccountCreatedNotification> Log => _log.AsReadOnly();
    public static void Clear() => _log.Clear();

    public Task Handle(AccountCreatedNotification notification, CancellationToken ct)
    {
        _log.Add(notification);
        return Task.CompletedTask;
    }
}
