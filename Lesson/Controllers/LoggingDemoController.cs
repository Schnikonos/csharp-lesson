using Microsoft.AspNetCore.Mvc;
using Lesson.UnitOfWork;

namespace Lesson.Controllers;

/// <summary>
/// Lesson 15-A — Built-in ILogger&lt;T&gt;: log levels, structured templates, log scopes.
///
/// ILogger&lt;T&gt; is already registered by the ASP.NET Core host — no extra packages needed.
///
/// Key concepts:
///   • Log levels:           Trace < Debug < Information < Warning < Error < Critical
///   • Message templates:    {AccountId} not string interpolation — the value is stored
///                           separately for structured log targets (JSON, Seq, etc.)
///   • Log scopes:           Attach ambient context to all log entries within a block
///   • appsettings filtering: "Logging:LogLevel:Default" controls minimum level per category
///
/// Java parallel:
///   ILogger&lt;T&gt;                → SLF4J Logger / Log4j2 Logger
///   LogInformation(...)       → logger.info(...)
///   LogWarning(...)           → logger.warn(...)
///   LogError(ex, ...)         → logger.error(message, exception)
///   BeginScope(...)           → MDC.put(...) / ThreadContext.put(...)
/// </summary>
[ApiController]
[Route("logging-demo")]
public class LoggingDemoController(
    ILogger<LoggingDemoController> logger,
    IUnitOfWork                    uow) : ControllerBase
{
    // ── GET /logging-demo/accounts/{id} ───────────────────────────────────────
    [HttpGet("accounts/{id:int}")]
    public async Task<IActionResult> GetAccount(int id)
    {
        // Structured template: {AccountId} is a named property, not a string format hole.
        // In JSON sinks (Seq, Elastic) the value is queryable: AccountId == 42
        logger.LogInformation("Fetching account {AccountId}", id);

        var account = await uow.Accounts.GetByIdAsync(id);

        if (account is null)
        {
            // LogWarning with structured context
            logger.LogWarning("Account {AccountId} not found", id);
            return NotFound();
        }

        logger.LogInformation("Account {AccountId} fetched, balance {Balance}", id, account.Balance);
        return Ok(account);
    }

    // ── POST /logging-demo/transfer — log scope example ───────────────────────
    [HttpPost("transfer")]
    public async Task<IActionResult> Transfer([FromBody] TransferLogRequest request)
    {
        // BeginScope attaches key-value pairs to ALL log entries inside the using block.
        // Java parallel: MDC.put("correlationId", id) / MDC.put("from", ...)
        using (logger.BeginScope(new Dictionary<string, object>
        {
            ["TransferId"] = Guid.NewGuid(),
            ["From"]       = request.From,
            ["To"]         = request.To,
        }))
        {
            logger.LogInformation("Starting transfer of {Amount}", request.Amount);

            var from = await uow.Accounts.GetByIdAsync(request.From);
            var to   = await uow.Accounts.GetByIdAsync(request.To);

            if (from is null || to is null)
            {
                logger.LogWarning("Transfer failed — account not found (from={From} to={To})",
                    request.From, request.To);
                return NotFound(new { error = "One or both accounts not found." });
            }

            if (from.Balance < request.Amount)
            {
                logger.LogWarning("Transfer failed — insufficient funds (balance={Balance} requested={Amount})",
                    from.Balance, request.Amount);
                return BadRequest(new { error = "Insufficient funds." });
            }

            from.Balance -= request.Amount;
            to.Balance   += request.Amount;
            await uow.CommitAsync();

            logger.LogInformation("Transfer completed. From balance={FromBalance} To balance={ToBalance}",
                from.Balance, to.Balance);
        }

        return Ok(new { message = "Transfer completed." });
    }

    // ── GET /logging-demo/levels — showcase all log levels ────────────────────
    [HttpGet("levels")]
    public IActionResult LogAllLevels()
    {
        logger.LogTrace("Trace — most verbose, usually disabled in production");
        logger.LogDebug("Debug — developer diagnostics");
        logger.LogInformation("Information — normal operation");
        logger.LogWarning("Warning — something unexpected but recoverable");
        logger.LogError("Error — operation failed");
        logger.LogCritical("Critical — system-wide failure");
        return Ok(new { message = "All levels emitted. Check the console/log output." });
    }
}

public record TransferLogRequest(int From, int To, decimal Amount);
