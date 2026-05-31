using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Lesson.Controllers;

/// <summary>
/// Lesson 06-C Extended — ASP.NET Core built-in Rate Limiting.
///
/// Rate limiting protects downstream resources and ensures fair usage.
/// Each endpoint demonstrates a different rate-limiting algorithm.
///
/// Java parallel:
///   Resilience4j RateLimiter / Bucket4j / Spring Cloud Gateway RequestRateLimiter filter
///   RateLimiterConfig.custom().limitForPeriod(10).limitRefreshPeriod(Duration.ofSeconds(10))
/// </summary>
[ApiController]
[Route("rate-demo")]
public class RateLimitController : ControllerBase
{
    /// <summary>
    /// GET /rate-demo/fixed
    ///
    /// Fixed window: up to 10 requests allowed per 10-second window.
    /// At the boundary, the counter resets immediately — can produce a burst
    /// of up to 2× limit if timed correctly (end of old window + start of new).
    ///
    /// Java parallel: Bucket4j Bandwidth.simple(10, Duration.ofSeconds(10))
    /// </summary>
    [HttpGet("fixed")]
    [EnableRateLimiting("fixed")]
    public IActionResult Fixed() =>
        Ok(new { policy = "fixed", message = "within fixed-window limit" });

    /// <summary>
    /// GET /rate-demo/sliding
    ///
    /// Sliding window: smoother than fixed — the window slides in sub-windows
    /// so there is never a hard-reset burst.
    ///
    /// Java parallel: Resilience4j SlidingWindowRateLimiter
    /// </summary>
    [HttpGet("sliding")]
    [EnableRateLimiting("sliding")]
    public IActionResult Sliding() =>
        Ok(new { policy = "sliding", message = "within sliding-window limit" });

    /// <summary>
    /// GET /rate-demo/token
    ///
    /// Token bucket: burst-friendly — clients can accumulate tokens up to the
    /// bucket capacity and spend them in a burst. Tokens refill steadily.
    ///
    /// Java parallel: Resilience4j SemaphoreBased RateLimiter / Guava RateLimiter
    /// </summary>
    [HttpGet("token")]
    [EnableRateLimiting("token")]
    public IActionResult Token() =>
        Ok(new { policy = "token", message = "within token-bucket limit" });
}
