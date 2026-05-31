using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Lesson.UnitOfWork;

namespace Lesson.Controllers;

/// <summary>
/// Lesson 15-C — OpenTelemetry: traces, metrics, ActivitySource, and health checks.
///
/// Key concepts:
///   1. ActivitySource  — the .NET API for creating custom spans (traces).
///                        Wraps System.Diagnostics.Activity; OTel SDK picks it up automatically.
///   2. Activity tags   — key-value metadata attached to a span (OTel "attributes").
///   3. Activity events — log-like timestamped annotations inside a span.
///   4. /health         — built-in health check endpoint wired to BankingDbContext.
///
/// Java parallels:
///   ActivitySource / Activity   → io.opentelemetry.api.trace.Tracer / Span
///   activity.SetTag(k, v)       → span.setAttribute(k, v)
///   activity.AddEvent(...)      → span.addEvent(...)
///   Activity.SetStatus(Error)   → span.setStatus(StatusCode.ERROR, description)
///   /health endpoint             → Spring Boot Actuator /actuator/health
/// </summary>
[ApiController]
[Route("otel-demo")]
public class OtelDemoController(
    ILogger<OtelDemoController> logger,
    IUnitOfWork                 uow) : ControllerBase
{
    // ActivitySource is the factory for creating Activity (span) objects.
    // The name ("BankingApi") must match the source registered with AddSource() in OTel config.
    // Java parallel: OpenTelemetry.getTracer("BankingApi")
    private static readonly ActivitySource Source = new("BankingApi");

    // ── GET /otel-demo/accounts/{id} ──────────────────────────────────────────
    // Creates a custom span around the DB call so it appears as a child span
    // in Jaeger / Zipkin under the parent HTTP request span.
    [HttpGet("accounts/{id:int}")]
    public async Task<IActionResult> GetAccount(int id)
    {
        // StartActivity creates a child span inside the current trace context.
        // If no parent is active the activity becomes the root span.
        // Java parallel: tracer.spanBuilder("FetchAccount").startSpan()
        using var activity = Source.StartActivity("FetchAccount");
        activity?.SetTag("account.id", id);         // OTel attribute — queryable in Jaeger
        activity?.AddEvent(new ActivityEvent("db.query.started"));

        var account = await uow.Accounts.GetByIdAsync(id);

        if (account is null)
        {
            // Mark span as failed — visible as red span in Jaeger
            // Java parallel: span.setStatus(StatusCode.ERROR, "account not found")
            activity?.SetStatus(ActivityStatusCode.Error, "account not found");
            logger.LogWarning("OTel: Account {AccountId} not found", id);
            return NotFound();
        }

        activity?.AddEvent(new ActivityEvent("db.query.completed"));
        activity?.SetTag("account.balance", (double)account.Balance);
        logger.LogInformation("OTel: Account {AccountId} fetched, Balance={Balance}", id, account.Balance);
        return Ok(account);
    }

    // ── POST /otel-demo/transfer — multi-span trace ────────────────────────────
    [HttpPost("transfer")]
    public async Task<IActionResult> Transfer([FromBody] TransferOtelRequest request)
    {
        // Parent span for the whole transfer operation
        using var transferActivity = Source.StartActivity("Transfer");
        transferActivity?.SetTag("transfer.from", request.From);
        transferActivity?.SetTag("transfer.to",   request.To);
        transferActivity?.SetTag("transfer.amount", (double)request.Amount);

        // Child span for the debit step
        using (var debitActivity = Source.StartActivity("DebitSource"))
        {
            debitActivity?.SetTag("account.id", request.From);
            var from = await uow.Accounts.GetByIdAsync(request.From);
            if (from is null)
            {
                debitActivity?.SetStatus(ActivityStatusCode.Error, "source account not found");
                return NotFound(new { error = $"Account {request.From} not found." });
            }
            if (from.Balance < request.Amount)
            {
                debitActivity?.SetStatus(ActivityStatusCode.Error, "insufficient funds");
                return BadRequest(new { error = "Insufficient funds." });
            }
            from.Balance -= request.Amount;
        }

        // Child span for the credit step
        using (var creditActivity = Source.StartActivity("CreditDestination"))
        {
            creditActivity?.SetTag("account.id", request.To);
            var to = await uow.Accounts.GetByIdAsync(request.To);
            if (to is null)
            {
                creditActivity?.SetStatus(ActivityStatusCode.Error, "destination account not found");
                return NotFound(new { error = $"Account {request.To} not found." });
            }
            to.Balance += request.Amount;
        }

        await uow.CommitAsync();
        logger.LogInformation("OTel: Transfer {Amount} from {From} to {To} completed",
            request.Amount, request.From, request.To);

        return Ok(new { message = "Transfer completed." });
    }
}

public record TransferOtelRequest(int From, int To, decimal Amount);
