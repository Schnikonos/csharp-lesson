namespace Lesson.Middleware;

/// <summary>
/// Lesson 06-A — Request/Response logging middleware.
///
/// Logs the HTTP method, path, status code, and elapsed time for every request.
/// Demonstrates:
///   • Implementing IMiddleware (recommended; lifetime managed by DI)
///   • Accessing HttpContext (Request / Response)
///   • Calling the next delegate in the pipeline
///   • Measuring elapsed time with Stopwatch
///
/// Java parallel: OncePerRequestFilter — override doFilterInternal(),
///   call filterChain.doFilter(), then log after the chain returns.
/// </summary>
public class RequestLoggingMiddleware(ILogger<RequestLoggingMiddleware> logger) : IMiddleware
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        logger.LogInformation("→ {Method} {Path}", context.Request.Method, context.Request.Path);

        await next(context);

        sw.Stop();
        logger.LogInformation("← {Method} {Path} {StatusCode} ({Elapsed}ms)",
            context.Request.Method,
            context.Request.Path,
            context.Response.StatusCode,
            sw.ElapsedMilliseconds);
    }
}
