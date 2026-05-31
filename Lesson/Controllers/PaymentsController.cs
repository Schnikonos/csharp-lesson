using Lesson.Commands;
using Lesson.Exceptions;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Lesson.Controllers;

/// <summary>
/// Lesson 07-C — Domain exceptions + MediatR pipeline demo.
///
/// Demonstrates:
///   • Throwing DomainException subclasses → DomainExceptionHandler → correct HTTP status
///   • MediatR command dispatch → ValidationBehavior → handler
///   • FluentValidation in the MediatR pipeline (ValidationException → 400)
/// </summary>
[ApiController]
[Route("payments")]
public class PaymentsController(IMediator mediator) : ControllerBase
{
    // POST /payments — dispatches CreatePaymentCommand through the MediatR pipeline
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreatePaymentCommand command,
        CancellationToken ct)
    {
        // ValidationBehavior runs FluentValidation before the handler.
        // DomainExceptionHandler catches BusinessRuleException from the handler.
        var result = await mediator.Send(command, ct);
        return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    }

    // GET /payments/{id} — simulates a not-found scenario
    [HttpGet("{id:guid}")]
    public IActionResult Get(Guid id)
    {
        // Simulate: payment not persisted in this lesson — always throw NotFoundException
        throw new NotFoundException("Payment", id);
    }

    // GET /payments/forbidden — simulates a forbidden scenario
    [HttpGet("forbidden")]
    public IActionResult Forbidden() =>
        throw new ForbiddenException("You do not have access to this resource.");
}
