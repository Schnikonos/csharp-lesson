using Lesson.Domain;
using Microsoft.AspNetCore.Mvc;

namespace Lesson.Controllers;

/// <summary>
/// Lesson 19-B — DDD controller: uses domain aggregate instead of raw repository.
///
/// Notice the business rules live INSIDE the aggregate now.
/// This controller cannot bypass invariants — it can only call the aggregate's
/// public operations (Deposit, Withdraw, etc.).
///
/// Java parallel:
///   Spring @RestController delegating to an @Service that loads the aggregate
///   via a Spring Data repository, calls domain methods, then saves.
/// </summary>
[ApiController]
[Route("ddd/accounts")]
public class DddAccountsController(IAggregateRepository repo) : ControllerBase
{
    // POST /ddd/accounts — opens a new account via the aggregate factory
    [HttpPost]
    public async Task<IActionResult> Open(
        [FromBody] OpenAccountRequest req,
        CancellationToken ct)
    {
        var aggregate = BankAccountAggregate.Open(
            req.AccountNumber,
            req.OwnerName,
            new Money(req.InitialBalance, "USD"));

        await repo.AddAsync(aggregate, ct);
        return CreatedAtAction(nameof(GetById), new { id = aggregate.Id }, new { aggregate.Id });
    }

    // GET /ddd/accounts/{id}
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id, CancellationToken ct)
    {
        var account = await repo.FindByIdAsync(id, ct);
        return account is null
            ? NotFound(new { id })
            : Ok(new { account.Id, account.AccountNumber, account.OwnerName, Balance = account.Balance.ToString() });
    }

    // POST /ddd/accounts/{id}/deposit — domain operation
    [HttpPost("{id:int}/deposit")]
    public async Task<IActionResult> Deposit(
        int id,
        [FromBody] DepositRequest req,
        CancellationToken ct)
    {
        var account = await repo.FindByIdAsync(id, ct);
        if (account is null) return NotFound(new { id });

        account.Deposit(new Money(req.Amount, "USD"));
        await repo.SaveAsync(account, ct);

        return Ok(new { account.Id, Balance = account.Balance.ToString() });
    }

    // POST /ddd/accounts/{id}/withdraw
    [HttpPost("{id:int}/withdraw")]
    public async Task<IActionResult> Withdraw(
        int id,
        [FromBody] WithdrawRequest req,
        CancellationToken ct)
    {
        var account = await repo.FindByIdAsync(id, ct);
        if (account is null) return NotFound(new { id });

        account.Withdraw(new Money(req.Amount, "USD"));
        await repo.SaveAsync(account, ct);

        return Ok(new { account.Id, Balance = account.Balance.ToString() });
    }
}

public record OpenAccountRequest(string AccountNumber, string OwnerName, decimal InitialBalance);
public record DepositRequest(decimal Amount);
public record WithdrawRequest(decimal Amount);
