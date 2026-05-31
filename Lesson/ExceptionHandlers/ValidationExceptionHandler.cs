using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Lesson.ExceptionHandlers;

/// <summary>
/// Lesson 07-C — FluentValidation.ValidationException handler.
///
/// When the MediatR ValidationBehavior throws a ValidationException,
/// this handler converts it to a 400 ValidationProblemDetails response —
/// the same shape [ApiController] produces for Data Annotations failures.
/// </summary>
public class ValidationExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not ValidationException validationEx)
            return false;

        var errors = validationEx.Errors
            .GroupBy(e => e.PropertyName)
            .ToDictionary(
                g => g.Key,
                g => g.Select(e => e.ErrorMessage).ToArray());

        var problem = new ValidationProblemDetails(errors)
        {
            Status = 400,
            Title  = "Validation failed.",
            Type   = "https://tools.ietf.org/html/rfc9110#section-15.5.1"
        };

        httpContext.Response.StatusCode = 400;
        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);

        return true;
    }
}
