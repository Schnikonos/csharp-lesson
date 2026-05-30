namespace Lesson.Exceptions;

/// <summary>
/// Lesson 07-C — Custom exception hierarchy.
///
/// DomainException is the base for all business-rule violations.
/// It carries an HTTP status code so the global exception handler can
/// map it to the correct response without a long if/else chain.
///
/// Java parallel: a custom RuntimeException hierarchy where each subclass
/// carries an HttpStatus; @ControllerAdvice maps them.
/// </summary>
public abstract class DomainException(string message, int statusCode = 400)
    : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

/// <summary>
/// Resource was not found — maps to 404.
/// Java parallel: throw new ResponseStatusException(HttpStatus.NOT_FOUND, ...)
/// </summary>
public class NotFoundException(string resource, object key)
    : DomainException($"{resource} '{key}' was not found.", 404);

/// <summary>
/// A business rule was violated — maps to 422 Unprocessable Entity.
/// Java parallel: ResponseStatusException(HttpStatus.UNPROCESSABLE_ENTITY)
/// </summary>
public class BusinessRuleException(string message)
    : DomainException(message, 422);

/// <summary>
/// Access to a resource was denied — maps to 403 Forbidden.
/// </summary>
public class ForbiddenException(string message)
    : DomainException(message, 403);
