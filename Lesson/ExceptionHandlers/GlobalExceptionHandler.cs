using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Lesson.ExceptionHandlers;

/// <summary>
/// Lesson 07-B — Global exception handler using IExceptionHandler (.NET 8+).
///
/// IExceptionHandler is the modern replacement for UseExceptionHandler(pipeline).
/// Multiple handlers can be registered; the pipeline stops at the first one
/// that returns true from TryHandleAsync.
///
/// This handler catches ALL unhandled exceptions and returns a ProblemDetails
/// response (RFC 7807) — a standardised JSON error format.
///
/// ProblemDetails shape:
/// {
///   "type":     "https://tools.ietf.org/html/rfc9110#section-15.6.1",
///   "title":    "An error occurred while processing your request.",
///   "status":   500,
///   "detail":   "...",
///   "traceId":  "00-abc..."
/// }
///
/// Java parallel: @ControllerAdvice + @ExceptionHandler(Exception.class)
///               returning ResponseEntity&lt;ProblemDetail&gt; (Spring 6+ RFC 9457 support).
/// </summary>
public class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        logger.LogError(exception, "Unhandled exception: {Message}", exception.Message);

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title  = "An unexpected error occurred.",
            Detail = exception.Message,
            Type   = "https://tools.ietf.org/html/rfc9110#section-15.6.1"
        };
        problem.Extensions["traceId"] =
            System.Diagnostics.Activity.Current?.Id ?? httpContext.TraceIdentifier;

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);

        return true; // handled — stop the handler pipeline
    }
}
