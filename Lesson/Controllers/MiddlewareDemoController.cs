using Microsoft.AspNetCore.Mvc;

namespace Lesson.Controllers;

/// <summary>
/// Lesson 06-A — Middleware demo endpoints.
///
/// These endpoints exist purely to let tests observe the middleware behaviour:
///   • The X-Powered-By response header added by ResponseHeaderMiddleware
///   • Logging side-effects of RequestLoggingMiddleware
/// </summary>
[ApiController]
[Route("middleware")]
public class MiddlewareDemoController : ControllerBase
{
    // GET /middleware/ping — used to verify ResponseHeaderMiddleware header is present
    [HttpGet("ping")]
    public IActionResult Ping() => Ok(new { message = "pong" });

    // GET /middleware/slow — simulates work so the logging elapsed time > 0
    [HttpGet("slow")]
    public async Task<IActionResult> Slow(CancellationToken ct)
    {
        await Task.Delay(10, ct);
        return Ok(new { message = "done" });
    }
}
