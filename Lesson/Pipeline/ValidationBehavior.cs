using FluentValidation;
using MediatR;

namespace Lesson.Pipeline;

/// <summary>
/// Lesson 07-C — MediatR validation pipeline behaviour.
///
/// IPipelineBehavior&lt;TRequest, TResponse&gt; is MediatR's middleware concept:
/// it wraps every IRequest handler in the same way ASP.NET Core middleware
/// wraps HTTP handlers.
///
/// This behaviour runs FluentValidation before the handler executes.
/// If validation fails it throws a ValidationException, which the global
/// exception handler (or a dedicated one) converts to a 400 response.
///
/// Java parallel: an AOP @Around advice on @Service methods annotated with
/// @Validated — Spring calls the JSR-380 validator before the method.
/// </summary>
public class ValidationBehavior<TRequest, TResponse>(
    IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (!validators.Any())
            return await next();

        var context = new ValidationContext<TRequest>(request);
        var failures = (await Task.WhenAll(
                validators.Select(v => v.ValidateAsync(context, cancellationToken))))
            .SelectMany(r => r.Errors)
            .Where(f => f is not null)
            .ToList();

        if (failures.Count > 0)
            throw new ValidationException(failures);

        return await next();
    }
}
