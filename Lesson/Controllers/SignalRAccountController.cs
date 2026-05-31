using Lesson.Data;
using Lesson.Hubs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Lesson.Controllers;

/// <summary>
/// Lesson 24-A — Controller that injects IHubContext to push real-time events.
///
/// IHubContext&lt;THub, TClient&gt; lets any service/controller broadcast to
/// connected clients without requiring a Hub method invocation from the client.
///
/// Java parallel:
///   SimpMessagingTemplate.convertAndSend("/topic/balance", event)
///   → hubContext.Clients.Group(...).ReceiveBalanceChanged(event)
/// </summary>
[ApiController]
[Route("signalr/accounts")]
public class SignalRAccountController(
    BankingDbContext db,
    IHubContext<BankingHub, IBankingClient> hub) : ControllerBase
{
    // POST /signalr/accounts/{id}/deposit
    // Deposits and broadcasts a BalanceChangedEvent to the account's SignalR group.
    [HttpPost("{id:int}/deposit")]
    public async Task<IActionResult> Deposit(int id, [FromBody] SignalRDepositBody body, CancellationToken ct)
    {
        var account = await db.BankAccounts.FindAsync([id], ct);
        if (account is null) return NotFound(new { id });
        if (body.Amount <= 0) return BadRequest(new { error = "Amount must be positive" });

        account.Balance += body.Amount;
        await db.SaveChangesAsync(ct);

        // Broadcast to all clients subscribed to this account's group
        // Java parallel: simpMessagingTemplate.convertAndSend("/topic/account-" + id, event)
        await hub.Clients
            .Group(BankingHub.GroupName(id))
            .ReceiveBalanceChanged(new BalanceChangedEvent(id, account.Balance, "deposit"));

        return Ok(new { account.Id, account.Balance });
    }
}

public record SignalRDepositBody(decimal Amount);
