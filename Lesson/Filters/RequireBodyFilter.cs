using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Lesson.Filters;

/// <summary>
/// Lesson 06-B — Short-circuiting action filter.
///
/// Validates that the incoming request body is not empty for POST/PUT endpoints.
/// If the body is null (e.g. missing Content-Type or no body), the filter returns
/// 400 Bad Request immediately — the action method is never invoked.
///
/// Demonstrates short-circuiting: setting context.Result before calling next()
/// stops the pipeline at this filter.
///
/// Java parallel: HandlerInterceptor.preHandle() returning false.
/// </summary>
public class RequireBodyFilter : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(
        ActionExecutingContext context,
        ActionExecutionDelegate next)
    {
        // Short-circuit if any action parameter bound from the body is null
        var hasNullBodyParam = context.ActionDescriptor.Parameters
            .Any(p => p.BindingInfo?.BindingSource?.Id == "Body"
                      && context.ActionArguments.TryGetValue(p.Name, out var val)
                      && val is null);

        if (hasNullBodyParam)
        {
            context.Result = new BadRequestObjectResult(
                new { error = "Request body is required." });
            return; // short-circuit — do NOT call next()
        }

        await next(); // continue to action
    }
}
