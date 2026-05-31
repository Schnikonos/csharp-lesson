using Lesson.EventSourcing;
using Microsoft.AspNetCore.Mvc;

namespace Lesson.Controllers;

/// <summary>
/// Lesson 25 — Event Sourcing
/// Demonstrates: commands write events; queries rehydrate from the event stream.
/// </summary>
[ApiController]
[Route("event-sourcing")]
public class EventSourcingController(IEventStore store) : ControllerBase
{
    // POST /event-sourcing/accounts
    [HttpPost("accounts")]
    public async Task<IActionResult> Open([FromBody] OpenEventSourcingAccountRequest req, CancellationToken ct)
    {
        var account = BankAccountAggregate.Open(req.AccountNumber, req.Owner, req.InitialBalance);
        await store.AppendAsync(account.Id, account.UncommittedEvents, ct);
        account.MarkCommitted();
        return CreatedAtAction(nameof(GetHistory), new { id = account.Id }, new { account.Id, account.Balance });
    }

    // POST /event-sourcing/accounts/{id}/deposit
    [HttpPost("accounts/{id:guid}/deposit")]
    public async Task<IActionResult> Deposit(Guid id, [FromBody] TransactionRequest req, CancellationToken ct)
    {
        var events = await store.LoadAsync(id, ct);
        if (events.Count == 0) return NotFound();

        var account = BankAccountAggregate.Rehydrate(events);
        account.Deposit(req.Amount, req.Description ?? string.Empty);
        await store.AppendAsync(id, account.UncommittedEvents, ct);
        account.MarkCommitted();
        return Ok(new { account.Balance });
    }

    // POST /event-sourcing/accounts/{id}/withdraw
    [HttpPost("accounts/{id:guid}/withdraw")]
    public async Task<IActionResult> Withdraw(Guid id, [FromBody] TransactionRequest req, CancellationToken ct)
    {
        var events = await store.LoadAsync(id, ct);
        if (events.Count == 0) return NotFound();

        var account = BankAccountAggregate.Rehydrate(events);
        try
        {
            account.Withdraw(req.Amount, req.Description ?? string.Empty);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        await store.AppendAsync(id, account.UncommittedEvents, ct);
        account.MarkCommitted();
        return Ok(new { account.Balance });
    }

    // GET /event-sourcing/accounts/{id}
    [HttpGet("accounts/{id:guid}")]
    public async Task<IActionResult> GetState(Guid id, CancellationToken ct)
    {
        var events = await store.LoadAsync(id, ct);
        if (events.Count == 0) return NotFound();
        var account = BankAccountAggregate.Rehydrate(events);
        return Ok(new { account.Id, account.AccountNumber, account.Owner, account.Balance, account.IsClosed, account.Version });
    }

    // GET /event-sourcing/accounts/{id}/history
    [HttpGet("accounts/{id:guid}/history")]
    public async Task<IActionResult> GetHistory(Guid id, CancellationToken ct)
    {
        var events = await store.LoadAsync(id, ct);
        if (events.Count == 0) return NotFound();
        var history = events.Select(e => new
        {
            Type = e.GetType().Name,
            e.OccurredAt,
            Payload = e
        });
        return Ok(history);
    }
}

public record OpenEventSourcingAccountRequest(string AccountNumber, string Owner, decimal InitialBalance);
public record TransactionRequest(decimal Amount, string? Description);
