using Lesson.Messaging.Contracts;
using Lesson.Messaging.Events;
using Lesson.Messaging.Sagas;
using MassTransit;
using Microsoft.AspNetCore.Mvc;

namespace Lesson.Controllers;

/// <summary>
/// Lesson 17-A/B/C — MassTransit publish, request/response, and saga patterns.
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
    [HttpGet("balance/{id:int}")]
    public async Task<IActionResult> GetBalance(int id, CancellationToken ct)
    {
        var response = await balanceClient.GetResponse<GetAccountBalanceResponse>(
            new GetAccountBalanceRequest(id), ct);
        if (!response.Message.Found) return NotFound(new { id });
        return Ok(response.Message);
    }

    // POST /messaging/transfer — kicks off the transfer saga
    // 17-C: A saga is a durable state machine coordinated through messages.
    // Java parallel: Axon Framework @Saga / Spring State Machine
    [HttpPost("transfer")]
    public async Task<IActionResult> InitiateTransfer(
        [FromBody] InitiateTransferRequest req,
        CancellationToken ct)
    {
        var correlationId = NewId.NextGuid();
        await publishEndpoint.Publish(new InitiateTransferCommand(
            correlationId, req.FromAccountId, req.ToAccountId, req.Amount), ct);

        return Accepted(new { correlationId, status = "initiated" });
    }
}

public record PublishAccountCreatedRequest(
    int     AccountId,
    string  AccountNumber,
    string  OwnerName,
    decimal InitialBalance);

public record InitiateTransferRequest(
    int     FromAccountId,
    int     ToAccountId,
    decimal Amount);
