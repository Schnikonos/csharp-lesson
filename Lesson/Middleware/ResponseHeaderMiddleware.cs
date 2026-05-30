namespace Lesson.Middleware;

/// <summary>
/// Lesson 06-A — Middleware ordering demo.
///
/// A second simple middleware that adds an <c>X-Powered-By</c> response header.
/// Its position in the pipeline (registered before or after other middleware) controls
/// whether downstream middleware can still modify the response.
///
/// Key insight: middleware registered FIRST wraps all subsequent middleware — it runs
/// before the next delegate on the way IN, and after on the way OUT.
///
/// Java parallel: OncePerRequestFilter — same "before / after filterChain.doFilter()" pattern.
/// </summary>
public class ResponseHeaderMiddleware : IMiddleware
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        // Before — add a header that will appear in every response
        context.Response.Headers["X-Powered-By"] = "ASP.NET Core 10 Lesson 06";

        await next(context);

        // After — response has already started; only safe to read, not to write body.
        // Status code is available here.
    }
}
