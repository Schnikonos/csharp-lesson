using MediatR;

namespace Lesson.Pipeline;

/// <summary>
/// Lesson 18-B — Transaction pipeline behaviour.
///
/// Wraps every ICommand&lt;T&gt; in an implicit commit scope.
/// Non-commands (IQuery) skip the commit step since they are read-only.
///
/// This keeps the handlers themselves free of transaction boilerplate:
/// the handler calls CommitAsync() itself in lesson 18-A, but in a real
/// project you could centralise the commit here and remove it from handlers.
///
/// Java parallel:
///   Spring @Transactional on a service method  →  this behaviour
///   The key difference: here the boundary is EXPLICIT and visible as a class.
/// </summary>
public class TransactionBehavior<TRequest, TResponse>(ILogger<TransactionBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest                          request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken                 ct)
    {
        // Only instrument commands (types that implement ICommand<>).
        // Queries pass straight through — they are side-effect free.
        bool isCommand = typeof(TRequest)
            .GetInterfaces()
            .Any(i => i.IsGenericType &&
                      i.GetGenericTypeDefinition() == typeof(Lesson.Cqrs.ICommand<>));

        if (!isCommand)
            return await next();

        logger.LogDebug("[Transaction] Opening implicit transaction for {Command}", typeof(TRequest).Name);
        var result = await next();
        logger.LogDebug("[Transaction] Command {Command} completed", typeof(TRequest).Name);
        return result;
    }
}
