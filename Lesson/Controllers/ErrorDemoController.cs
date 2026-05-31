using FluentValidation;
using Lesson.Models;
using Microsoft.AspNetCore.Mvc;

namespace Lesson.Controllers;

/// <summary>
/// Lesson 07-B — ProblemDetails, global exception handler, and FluentValidation.
///
/// Demonstrates:
///   • Unhandled exception → caught by GlobalExceptionHandler → ProblemDetails 500
///   • Manual FluentValidation in a controller action → 400 ValidationProblemDetails
///   • ProblemDetails shape (RFC 7807): type, title, status, detail, traceId
/// </summary>
[ApiController]
[Route("error-demo")]
public class ErrorDemoController(IValidator<CreateTransferRequest> validator) : ControllerBase
{
    // GET /error-demo/unhandled — throws with no try/catch → GlobalExceptionHandler handles it
    [HttpGet("unhandled")]
    public IActionResult Unhandled()
    {
        throw new InvalidOperationException("This was not caught by the action.");
    }

    // POST /error-demo/fluent-validate — uses FluentValidation manually
    [HttpPost("fluent-validate")]
    public async Task<IActionResult> FluentValidate(
        [FromBody] CreateTransferRequest request,
        CancellationToken ct)
    {
        var result = await validator.ValidateAsync(request, ct);
        if (!result.IsValid)
        {
            var errors = result.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(e => e.ErrorMessage).ToArray());

            return ValidationProblem(new ValidationProblemDetails(errors));
        }

        return Ok(new { message = "Transfer is valid.", amount = request.Amount });
    }
}
