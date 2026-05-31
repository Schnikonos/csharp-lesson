using Lesson.Messaging.Contracts;
using Lesson.Messaging.Events;
using MassTransit;
using Microsoft.AspNetCore.Mvc;

namespace Lesson.Controllers;

/// <summary>
/// Lesson 17-A/B — MassTransit publish and request/response patterns.
/// </summary>
[ApiController]
[Route("messaging")]
public class MessagingDemoController(
    IPublishEndpoint publishEndpoint,
    IRequestClient<GetAccountBalanceRequest> balanceClient) : ControllerBase
{
    // POST /messaging/account-created
    [HttpPost("account-created")]
    public async Task<IActionResult> PublishAccountCreated(
        [FromBody] PublishAccountCreatedRequest req,
        CancellationToken ct)
    {
        var evt = new AccountCreatedEvent(
            req.AccountId, req.AccountNumber, req.OwnerName,
            req.InitialBalance, DateTimeOffset.UtcNow);

        await publishEndpoint.Publish(evt, ct);
        return Accepted(new { published = true, evt.AccountId });
    }

    // GET /messaging/balance/{id}
    // Demonstrates request/response: controller sends a request and awaits the reply.
    // Java parallel: rabbitTemplate.convertSendAndReceive() / Spring Integration gateway
    [HttpGet("balance/{id:int}")]
    public async Task<IActionResult> GetBalance(int id, CancellationToken ct)
    {
        // IRequestClient sends to the consumer and awaits the typed response.
        // MassTransit handles correlation ID, reply queue, and timeout automatically.
        var response = await balanceClient.GetResponse<GetAccountBalanceResponse>(
            new GetAccountBalanceRequest(id), ct);

        if (!response.Message.Found)
            return NotFound(new { id });

        return Ok(response.Message);
    }
}

public record PublishAccountCreatedRequest(
    int     AccountId,
    string  AccountNumber,
    string  OwnerName,
    decimal InitialBalance);
