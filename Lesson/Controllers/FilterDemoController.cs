using Lesson.Filters;
using Microsoft.AspNetCore.Mvc;

namespace Lesson.Controllers;

/// <summary>
/// Lesson 06-B — Action Filter demo endpoints.
///
/// Demonstrates:
///   • CorrelationIdFilter applied globally — every response carries X-Correlation-Id
///   • RequireBodyFilter used per-action via [TypeFilter] — short-circuits on missing body
/// </summary>
[ApiController]
[Route("filters")]
public class FilterDemoController : ControllerBase
{
    // GET /filters/echo — echos the correlation ID back in the body (it's already in the header)
    [HttpGet("echo")]
    public IActionResult Echo()
    {
        var correlationId = HttpContext.Items[CorrelationIdFilter.ItemsKey]?.ToString();
        return Ok(new { correlationId });
    }

    // GET /filters/echo-header — lets the test pass X-Correlation-Id explicitly
    [HttpGet("echo-header")]
    public IActionResult EchoHeader([FromHeader(Name = CorrelationIdFilter.HeaderName)] string? correlationId)
        => Ok(new { correlationId });

    // POST /filters/body-required — short-circuits with 400 if body is missing
    [HttpPost("body-required")]
    [TypeFilter(typeof(RequireBodyFilter))]
    public IActionResult BodyRequired([FromBody] SamplePayload? payload)
        => Ok(new { received = payload?.Value });

    public record SamplePayload(string Value);
}
