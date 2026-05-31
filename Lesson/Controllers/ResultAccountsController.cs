using ErrorOr;
using Lesson.ResultPattern;
using Microsoft.AspNetCore.Mvc;

namespace Lesson.Controllers;

/// <summary>
/// Lesson 22-A — Controller that maps ErrorOr&lt;T&gt; to HTTP responses.
///
/// Pattern-matching on IsError eliminates try/catch; each Error type maps
/// to a standard ProblemDetails response.
///
/// Java parallel:
///   Vavr Either.fold(left → ResponseEntity.badRequest(), right → ResponseEntity.ok())
///   Spring @ExceptionHandler on a custom AppException hierarchy
/// </summary>
[ApiController]
[Route("result/accounts")]
public class ResultAccountsController(AccountResultService svc) : ControllerBase
{
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id, CancellationToken ct)
    {
        var result = await svc.GetByIdAsync(id, ct);
        return result.Match<IActionResult>(
            dto   => Ok(dto),
            errors => MapErrors(errors));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateResultAccountRequest req, CancellationToken ct)
    {
        var result = await svc.CreateAsync(req.AccountNumber, req.OwnerName, req.InitialBalance, ct);
        return result.Match<IActionResult>(
            dto    => Created($"/result/accounts/{dto.Id}", dto),
            errors => MapErrors(errors));
    }

    // ── Error → HTTP mapping ──────────────────────────────────────────────────
    private IActionResult MapErrors(List<Error> errors)
    {
        if (errors.Count == 0) return Problem();
        var first = errors[0];
        return first.Type switch
        {
            ErrorType.NotFound   => NotFound(ProblemDetailsFor(first)),
            ErrorType.Conflict   => Conflict(ProblemDetailsFor(first)),
            ErrorType.Validation => BadRequest(ProblemDetailsFor(first)),
            _                    => Problem(detail: first.Description),
        };
    }

    private static object ProblemDetailsFor(Error e) =>
        new { type = e.Code, detail = e.Description };
}

public record CreateResultAccountRequest(string AccountNumber, string OwnerName, decimal InitialBalance);
