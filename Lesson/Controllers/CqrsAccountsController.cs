using Lesson.Features.Accounts.Commands;
using Lesson.Features.Accounts.Queries;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Lesson.Controllers;

/// <summary>
/// Lesson 18-A — CQRS controller: dispatches commands and queries via MediatR ISender.
///
/// The controller has ZERO business logic. It:
///   1. Deserialises the HTTP request into a Command or Query object.
///   2. Hands it to ISender.Send() — MediatR routes to the handler.
///   3. Converts the handler result to an HTTP response.
///
/// Java parallel:
///   Spring @RestController delegating to @Service  →  here the "service" is replaced
///   by explicit command/query handlers wired through MediatR.
/// </summary>
[ApiController]
[Route("cqrs/accounts")]
public class CqrsAccountsController(ISender sender) : ControllerBase
{
    // POST /cqrs/accounts  — creates an account via a command
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateAccountCommand cmd,
        CancellationToken ct)
    {
        // ISender.Send dispatches to CreateAccountCommandHandler
        var id = await sender.Send(cmd, ct);
        return CreatedAtAction(nameof(GetAll), new { }, new { id });
    }

    // GET /cqrs/accounts  — returns all accounts via a query
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        // ISender.Send dispatches to GetAllAccountsQueryHandler
        var accounts = await sender.Send(new GetAllAccountsQuery(), ct);
        return Ok(accounts);
    }
}
