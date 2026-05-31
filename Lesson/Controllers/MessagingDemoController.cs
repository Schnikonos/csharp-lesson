using Lesson.Messaging.Events;
using MassTransit;
using Microsoft.AspNetCore.Mvc;

namespace Lesson.Controllers;

/// <summary>
/// Lesson 17-A — Demonstrates publishing a message via MassTransit's in-memory transport.
///
/// Key concepts:
///   IPublishEndpoint   — fire-and-forget publish to all subscribers.
///                        Java parallel: ApplicationEventPublisher / RabbitTemplate.convertAndSend()
///   ISendEndpointProvider — send to a specific address (queue name).
///   In-memory transport  — no broker required; ideal for unit/integration tests
///                          and in-process fan-out.
///
/// The consumer (AccountCreatedConsumer) receives the event on the same bus.
/// In production you would swap UsingInMemory() for UsingRabbitMq() without
/// changing this controller at all.
/// </summary>
[ApiController]
[Route("messaging")]
public class MessagingDemoController(IPublishEndpoint publishEndpoint) : ControllerBase
{
    // POST /messaging/account-created
    // Publishes an AccountCreatedEvent to all registered consumers.
    [HttpPost("account-created")]
    public async Task<IActionResult> PublishAccountCreated(
        [FromBody] PublishAccountCreatedRequest req,
        CancellationToken ct)
    {
        var evt = new AccountCreatedEvent(
            req.AccountId,
            req.AccountNumber,
            req.OwnerName,
            req.InitialBalance,
            DateTimeOffset.UtcNow);

        // IPublishEndpoint.Publish sends to every consumer subscribed to AccountCreatedEvent.
        // Java parallel: applicationEventPublisher.publishEvent(evt);
        await publishEndpoint.Publish(evt, ct);

        return Accepted(new { published = true, evt.AccountId });
    }
}

public record PublishAccountCreatedRequest(
    int     AccountId,
    string  AccountNumber,
    string  OwnerName,
    decimal InitialBalance);
