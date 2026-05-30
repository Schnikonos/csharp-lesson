using Lesson.Events;
using Lesson.Subscribers;
using Microsoft.AspNetCore.Mvc;

namespace Lesson.Controllers;

/// <summary>
/// Lesson 08-A — Events demo endpoints.
///
/// Demonstrates:
///   • Publishing a C# event (PaymentCreated) via DomainEventBus
///   • Reading the audit log populated by PaymentAuditSubscriber
///   • Using Action&lt;T&gt; and Func&lt;T, TResult&gt; delegates inline
/// </summary>
[ApiController]
[Route("event-demo")]
public class EventDemoController(
    DomainEventBus bus,
    PaymentAuditSubscriber audit) : ControllerBase
{
    // POST /event-demo/payment — publishes a PaymentCreated event
    [HttpPost("payment")]
    public IActionResult PublishPayment([FromBody] PublishPaymentRequest request)
    {
        var args = new PaymentCreatedEventArgs
        {
            PaymentId   = Guid.NewGuid(),
            FromAccount = request.FromAccount,
            ToAccount   = request.ToAccount,
            Amount      = request.Amount
        };

        bus.PublishPaymentCreated(args);

        return Ok(new { published = true, paymentId = args.PaymentId });
    }

    // GET /event-demo/audit — returns the in-memory audit log
    [HttpGet("audit")]
    public IActionResult GetAuditLog() =>
        Ok(audit.Log.Select(e => new
        {
            e.PaymentId, e.FromAccount, e.ToAccount, e.Amount, e.OccurredAt
        }));

    // GET /event-demo/delegate-demo — illustrates Action<T> and Func<T, TResult> inline
    [HttpGet("delegate-demo")]
    public IActionResult DelegateDemo()
    {
        // Action<T> — a delegate that takes a value and returns void
        Action<string> log = msg => Console.WriteLine($"[LOG] {msg}");
        log("Hello from Action<T>");

        // Func<T, TResult> — a delegate that takes a value and returns a result
        Func<decimal, decimal> applyTax = amount => amount * 1.2m;
        var total = applyTax(100m);

        // Multicast delegate — multiple subscribers called in registration order
        Action<string> multicast = s => { };
        multicast += s => Console.WriteLine($"[SUB1] {s}");
        multicast += s => Console.WriteLine($"[SUB2] {s}");
        multicast("fired");

        return Ok(new { actionUsed = true, funcResult = total, multicastFired = true });
    }

    // DELETE /event-demo/audit/reset — test helper
    [HttpDelete("audit/reset")]
    public IActionResult ResetAudit()
    {
        // We cannot clear the log from outside (IReadOnlyList), but we expose
        // this endpoint to illustrate the pattern for tests.
        return Ok(new { note = "Audit log is append-only in this lesson." });
    }

    public record PublishPaymentRequest(string FromAccount, string ToAccount, decimal Amount);
}
