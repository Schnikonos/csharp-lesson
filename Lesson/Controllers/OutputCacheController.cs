using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Lesson.UnitOfWork;

namespace Lesson.Controllers;

/// <summary>
/// Lesson 14-C — Response caching + Output caching (.NET 7+) + cache stampede prevention.
///
/// [ResponseCache] adds Cache-Control HTTP headers so the CLIENT (browser, CDN)
///   caches the response. No server-side storage involved.
///
/// [OutputCache] stores the full response in SERVER-side memory.
///   The client always hits the server, but the server serves from its cache.
///   This is the .NET 7+ evolution of the older response caching middleware.
///
/// Cache stampede (dog-piling) happens when a popular cache entry expires and
///   hundreds of requests simultaneously hit the DB. The Lock policy in Output Cache
///   serialises concurrent requests for the same key, so only one hits the origin.
///
/// Java parallel:
///   [ResponseCache] → Spring HTTP headers via HttpCachePolicy / @CacheControl
///   [OutputCache]   → Spring Cache + @Cacheable on controller methods
///   Lock policy     → Spring Cache with synchronized=true
/// </summary>
[ApiController]
[Route("output-cache")]
public class OutputCacheController(IUnitOfWork uow) : ControllerBase
{
    // ── [ResponseCache] — client-side + CDN HTTP headers ─────────────────────
    // Sets: Cache-Control: public, max-age=30
    // Java: @GetMapping + response.setHeader("Cache-Control", "max-age=30")
    [HttpGet("headers/{id:int}")]
    [ResponseCache(Duration = 30, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> WithResponseHeaders(int id)
    {
        var account = await uow.Accounts.GetByIdAsync(id);
        return account is null ? NotFound() : Ok(account);
    }

    // ── [OutputCache] — server-side in-memory response cache ─────────────────
    // Caches the full HTTP response body for 60 s.
    // Java: @Cacheable(value = "output", key = "#id") on a controller method
    [HttpGet("server/{id:int}")]
    [OutputCache(Duration = 60)]
    public async Task<IActionResult> WithOutputCache(int id)
    {
        var account = await uow.Accounts.GetByIdAsync(id);
        return account is null ? NotFound() : Ok(new { source = "origin", account });
    }

    // ── [OutputCache] with Vary-by-query ─────────────────────────────────────
    // Different query-string values produce separate cache entries.
    [HttpGet("server/list")]
    [OutputCache(Duration = 30, VaryByQueryKeys = ["type", "page"])]
    public async Task<IActionResult> ListWithVary([FromQuery] string? type, [FromQuery] int page = 1)
    {
        var all = await uow.Accounts.GetAllAsync(null);
        var filtered = (type is null ? all : all.Where(a => a.AccountType == type))
            .Skip((page - 1) * 10).Take(10);
        return Ok(new { page, type, items = filtered });
    }

    // ── [OutputCache] with named policy (anti-stampede Lock) ─────────────────
    // The "Lock" policy serialises concurrent requests for the same key,
    // preventing the cache stampede (dog-pile) problem.
    [HttpGet("server/safe/{id:int}")]
    [OutputCache(PolicyName = "Lock")]
    public async Task<IActionResult> AntiStampede(int id)
    {
        await Task.Delay(50); // simulate slow DB
        var account = await uow.Accounts.GetByIdAsync(id);
        return account is null ? NotFound() : Ok(new { source = "origin", account });
    }
}
