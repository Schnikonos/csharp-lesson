using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Lesson.Filters;

/// <summary>
/// Lesson 06-C — IResourceFilter: runs around model binding.
///
/// IResourceFilter has two hooks:
///   OnResourceExecuting  — before model binding (can short-circuit or cache)
///   OnResourceExecuted   — after the full action pipeline (including result execution)
///
/// This implementation adds a simple in-process response cache keyed by
/// "{Method}:{Path}?{QueryString}".  On a cache HIT the action method is never
/// called — the filter short-circuits by writing the cached result directly.
///
/// Java parallel: a Servlet Filter that inspects ETag / Last-Modified and
/// returns 304 without invoking the controller.
/// </summary>
public class ResponseCacheFilter : IResourceFilter
{
    private static readonly Dictionary<string, IActionResult> _cache = new();

    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        var key = CacheKey(context.HttpContext);
        if (_cache.TryGetValue(key, out var cached))
        {
            context.Result = cached; // short-circuit — skips model binding + action
        }
    }

    public void OnResourceExecuted(ResourceExecutedContext context)
    {
        var key = CacheKey(context.HttpContext);
        if (context.Result is ObjectResult result && !_cache.ContainsKey(key))
        {
            _cache[key] = result; // store for next request
        }
    }

    public static void ClearCache() => _cache.Clear();

    private static string CacheKey(HttpContext ctx) =>
        $"{ctx.Request.Method}:{ctx.Request.Path}{ctx.Request.QueryString}";
}
