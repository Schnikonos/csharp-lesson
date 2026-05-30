using Lesson.Filters;
using Microsoft.AspNetCore.Mvc;

namespace Lesson.Controllers;

/// <summary>
/// Lesson 06-C — Advanced Filter demo endpoints.
///
/// Demonstrates:
///   • [TypeFilter(typeof(ResponseCacheFilter))] — IResourceFilter that caches responses
///   • [TypeFilter(typeof(EnvelopeResultFilter))] — IResultFilter that wraps the result
///   • A minimal-API endpoint (registered in Program.cs) protected by IEndpointFilter
/// </summary>
[ApiController]
[Route("advanced-filters")]
public class AdvancedFilterController : ControllerBase
{
    private static int _callCount; // tracks whether cache short-circuited the action

    // GET /advanced-filters/cached — action only executes on first call
    [HttpGet("cached")]
    [TypeFilter(typeof(ResponseCacheFilter))]
    public IActionResult Cached()
    {
        _callCount++;
        return Ok(new { callCount = _callCount, cached = false });
    }

    // GET /advanced-filters/reset-cache
    [HttpDelete("reset-cache")]
    public IActionResult ResetCache()
    {
        ResponseCacheFilter.ClearCache();
        _callCount = 0;
        return Ok(new { reset = true });
    }

    // GET /advanced-filters/envelope — wraps the result in { data, meta }
    [HttpGet("envelope")]
    [TypeFilter(typeof(EnvelopeResultFilter))]
    public IActionResult Envelope() => Ok(new { value = 42 });
}
