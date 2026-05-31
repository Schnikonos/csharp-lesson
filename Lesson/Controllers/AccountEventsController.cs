using Lesson.Handlers;
using Lesson.Notifications;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Lesson.Controllers;

/// <summary>
/// Lesson 08-B — MediatR INotification demo endpoints.
///
/// Demonstrates:
///   • IMediator.Publish(notification) — sends to all registered INotificationHandler&lt;T&gt;
///   • Multiple handlers receiving the same notification
///   • Decoupled publisher — controller knows nothing about the handlers
/// </summary>
[ApiController]
[Route("accounts-events")]
public class AccountEventsController(IMediator mediator) : ControllerBase
{
    // POST /accounts-events — creates an account and publishes AccountCreatedNotification
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateAccountRequest request,
        CancellationToken ct)
    {
        var notification = new AccountCreatedNotification(
            AccountId: Guid.NewGuid(),
            OwnerName: request.OwnerName,
            InitialBalance: request.InitialBalance);

        // Publish to ALL registered INotificationHandler<AccountCreatedNotification>
        // Both SendWelcomeEmailHandler and AccountCreatedAuditHandler will receive this.
        await mediator.Publish(notification, ct);

        return Ok(new { notification.AccountId, notification.OwnerName });
    }

    // GET /accounts-events/audit — returns the static audit log
    [HttpGet("audit")]
    public IActionResult Audit() =>
        Ok(AccountCreatedAuditHandler.Log.Select(n =>
            new { n.AccountId, n.OwnerName, n.InitialBalance }));

    // DELETE /accounts-events/audit/reset — test helper
    [HttpDelete("audit/reset")]
    public IActionResult ResetAudit()
    {
        AccountCreatedAuditHandler.Clear();
        return NoContent();
    }

    public record CreateAccountRequest(string OwnerName, decimal InitialBalance);
}
