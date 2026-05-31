using ErrorOr;
using Lesson.ResultPattern;
using Microsoft.AspNetCore.Mvc;

namespace Lesson.Controllers;

/// <summary>
/// Lesson 22-B — Controller using the railway-oriented service.
///
/// The MapToActionResult extension method maps ErrorOr&lt;T&gt; to IActionResult,
/// keeping controller action bodies to a single expression.
///
/// Java parallel:
///   ResponseEntity.of(Optional) / Either.fold(...)
/// </summary>
[ApiController]
[Route("result/pipeline")]
public class ResultPipelineController(AccountRopService rop) : ControllerBase
{
    // POST /result/pipeline/{id}/deposit
    [HttpPost("{id:int}/deposit")]
    public async Task<IActionResult> Deposit(int id, [FromBody] DepositBody body, CancellationToken ct)
    {
        var result = await rop.DepositAsync(id, body.Amount, ct);
        return result.ToActionResult(this);
    }

    // POST /result/pipeline/transfer
    [HttpPost("transfer")]
    public async Task<IActionResult> Transfer([FromBody] TransferBody body, CancellationToken ct)
    {
        var result = await rop.TransferAsync(body.FromId, body.ToId, body.Amount, ct);
        return result.ToActionResult(this);
    }
}

public record DepositBody(decimal Amount);
public record TransferBody(int FromId, int ToId, decimal Amount);

/// <summary>
/// Lesson 22-B — Extension method: maps ErrorOr&lt;T&gt; → IActionResult.
/// Keeps controller actions clean; re-uses the same mapping logic everywhere.
/// </summary>
public static class ErrorOrExtensions
{
    public static IActionResult ToActionResult<T>(this ErrorOr<T> result, ControllerBase ctrl) =>
        result.Match<IActionResult>(
            value  => ctrl.Ok(value),
            errors =>
            {
                var first = errors[0];
                return first.Type switch
                {
                    ErrorType.NotFound   => ctrl.NotFound(new { first.Code, first.Description }),
                    ErrorType.Validation => ctrl.BadRequest(new { first.Code, first.Description }),
                    ErrorType.Conflict   => ctrl.Conflict(new { first.Code, first.Description }),
                    _                    => ctrl.Problem(first.Description),
                };
            });
}
