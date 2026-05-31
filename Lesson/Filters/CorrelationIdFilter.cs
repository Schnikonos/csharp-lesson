using Microsoft.AspNetCore.Mvc.Filters;

namespace Lesson.Filters;

/// <summary>
/// Lesson 06-B — Correlation ID action filter.
///
/// Reads X-Correlation-Id from the incoming request (or generates a new Guid),
/// stores it in HttpContext.Items, and echoes it back in the response headers.
///
/// Applied globally via AddControllers().AddMvcOptions() in Program.cs.
///
/// Java parallel: HandlerInterceptor.preHandle() — read/set header, store in
/// ThreadLocal or request attributes; postHandle() — write to response.
/// </summary>
public class CorrelationIdFilter : IActionFilter
{
    public const string HeaderName = "X-Correlation-Id";
    public const string ItemsKey   = "CorrelationId";

    // Runs BEFORE the action method executes
    public void OnActionExecuting(ActionExecutingContext context)
    {
        var correlationId = context.HttpContext.Request.Headers[HeaderName].FirstOrDefault()
                            ?? Guid.NewGuid().ToString();

        // Store for downstream use (e.g. logging enrichment)
        context.HttpContext.Items[ItemsKey] = correlationId;

        // Echo back in response
        context.HttpContext.Response.Headers[HeaderName] = correlationId;
    }

    // Runs AFTER the action method executes (before result execution)
    public void OnActionExecuted(ActionExecutedContext context)
    {
        // Nothing extra needed — header already set in OnActionExecuting.
        // This hook is available for post-processing: modify the result, log exceptions, etc.
    }
}
