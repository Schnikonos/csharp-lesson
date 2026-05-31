using MediatR;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Lesson.Pipeline;

/// <summary>
/// Lesson 18-B — Logging pipeline behaviour.
///
/// IPipelineBehavior&lt;TRequest, TResponse&gt; wraps EVERY MediatR request/response pair,
/// similar to middleware in ASP.NET Core but scoped to the MediatR pipeline.
///
/// Execution order is determined by registration order in DI.
/// This behaviour runs OUTERMOST so it sees the full elapsed time.
///
/// Java parallel:
///   Spring AOP @Around("@annotation(Transactional)")  →  IPipelineBehavior&lt;,&gt;
///   Axon MessageHandlerInterceptor                    →  IPipelineBehavior&lt;,&gt;
/// </summary>
public class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest                          request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken                 ct)
    {
        var name = typeof(TRequest).Name;
        var sw   = Stopwatch.StartNew();

        logger.LogInformation("Handling {Request}", name);
        try
        {
            var response = await next();          // call the next behaviour / handler
            logger.LogInformation("Handled {Request} in {Elapsed}ms", name, sw.ElapsedMilliseconds);
            return response;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling {Request}", name);
            throw;
        }
    }
}
