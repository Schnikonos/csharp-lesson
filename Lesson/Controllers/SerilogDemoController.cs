using Microsoft.AspNetCore.Mvc;
using Serilog.Context;
using Lesson.UnitOfWork;

namespace Lesson.Controllers;

/// <summary>
/// Lesson 15-B — Serilog: sinks, enrichers, and correlation ID propagation.
///
/// Key concepts demonstrated:
///   1. Serilog is wired via builder.Host.UseSerilog() — all ILogger&lt;T&gt; calls flow through it.
///   2. Correlation ID is pushed onto LogContext at the start of each request so every
///      downstream log entry in that request automatically includes it.
///   3. IHttpContextAccessor lets any service (not just controllers) read request metadata.
///
/// Java parallels:
///   Serilog.AspNetCore            → Logback / Log4j2 configured via logback.xml
///   Serilog sinks                  → Appenders (ConsoleAppender, RollingFileAppender)
///   Enrich.FromLogContext()        → MDC automatic propagation
///   LogContext.PushProperty(...)   → MDC.put(key, value)
///   IHttpContextAccessor           → HttpServletRequest / @RequestScope beans
/// </summary>
[ApiController]
[Route("serilog-demo")]
public class SerilogDemoController(
    ILogger<SerilogDemoController>  logger,
    IUnitOfWork                     uow,
    IHttpContextAccessor            httpContextAccessor) : ControllerBase
{
    // ── GET /serilog-demo/accounts/{id} ───────────────────────────────────────
    // Pushes CorrelationId onto the Serilog LogContext so every log entry
    // emitted while handling this request carries the property.
    [HttpGet("accounts/{id:int}")]
    public async Task<IActionResult> GetAccount(int id)
    {
        var correlationId = httpContextAccessor.HttpContext?
            .Request.Headers["X-Correlation-ID"].FirstOrDefault()
            ?? Guid.NewGuid().ToString("N");

        // PushProperty returns an IDisposable — the property is removed when the scope ends.
        // Java parallel: MDC.put("correlationId", correlationId)  +  MDC.remove(...)
        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            logger.LogInformation("Fetching account {AccountId}", id);

            var account = await uow.Accounts.GetByIdAsync(id);

            if (account is null)
            {
                logger.LogWarning("Account {AccountId} not found", id);
                return NotFound();
            }

            logger.LogInformation("Account {AccountId} found, Balance={Balance}", id, account.Balance);
            return Ok(account);
        }
    }

    // ── GET /serilog-demo/enrich — show all enriched properties at once ────────
    [HttpGet("enrich")]
    public IActionResult ShowEnrichers()
    {
        var correlationId = httpContextAccessor.HttpContext?
            .Request.Headers["X-Correlation-ID"].FirstOrDefault()
            ?? Guid.NewGuid().ToString("N");

        using (LogContext.PushProperty("CorrelationId",  correlationId))
        using (LogContext.PushProperty("UserId",         "demo-user"))
        using (LogContext.PushProperty("ServiceVersion", "1.0.0"))
        {
            logger.LogInformation(
                "Enriched log entry emitted — check console for CorrelationId, UserId, ServiceVersion");
        }

        return Ok(new
        {
            correlationId,
            message = "Enriched log entry emitted. Check server console for structured properties."
        });
    }
}
