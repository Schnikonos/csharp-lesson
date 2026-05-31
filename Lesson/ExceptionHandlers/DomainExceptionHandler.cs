using Lesson.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Lesson.ExceptionHandlers;

/// <summary>
/// Lesson 07-C — Domain exception handler.
///
/// Registered BEFORE GlobalExceptionHandler in Program.cs so it runs first.
/// Handles any DomainException subclass and maps it to the appropriate
/// HTTP status code carried by the exception itself.
///
/// The GlobalExceptionHandler acts as the final fallback for unknown exceptions.
///
/// Java parallel: @ExceptionHandler(DomainException.class) on a @ControllerAdvice —
/// Spring calls the most-specific matching handler first.
/// </summary>
public class DomainExceptionHandler(ILogger<DomainExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not DomainException domainEx)
            return false; // not our concern — let GlobalExceptionHandler handle it

        logger.LogWarning("Domain exception {Type}: {Message}",
            domainEx.GetType().Name, domainEx.Message);

        var problem = new ProblemDetails
        {
            Status = domainEx.StatusCode,
            Title  = domainEx.GetType().Name,
            Detail = domainEx.Message,
            Type   = $"https://example.com/errors/{domainEx.GetType().Name.ToLower()}"
        };

        httpContext.Response.StatusCode = domainEx.StatusCode;
        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);

        return true; // handled
    }
}
