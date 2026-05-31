using Microsoft.AspNetCore.Mvc;
using Lesson.UnitOfWork;

namespace Lesson.Controllers;

/// <summary>
/// Lesson 16-A — Task, async/await deep dive, CancellationToken patterns.
///
/// Key concepts demonstrated:
///   1. Task.Run          — offloads CPU-bound work to the thread pool (use sparingly in ASP.NET Core;
///                          prefer naturally-async IO methods instead).
///   2. Task.WhenAll      — fan-out: start N tasks concurrently, await all completions.
///   3. Task.WhenAny      — race: return as soon as the *first* task completes (e.g. timeout pattern).
///   4. CancellationToken — cooperative cancellation propagated from the HTTP request lifecycle;
///                          the framework cancels the token when the client disconnects.
///   5. ConfigureAwait    — library code should use ConfigureAwait(false) to avoid capturing the
///                          ASP.NET Core SynchronizationContext (there isn't one in .NET 5+, but
///                          it matters for library portability and avoidance of deadlocks in
///                          legacy sync-over-async call sites).
///
/// Java parallels:
///   Task                  → CompletableFuture / Future
///   async/await           → CompletableFuture.thenCompose / .thenApply
///   Task.Run              → CompletableFuture.supplyAsync(supplier, executor)
///   Task.WhenAll          → CompletableFuture.allOf(...)
///   Task.WhenAny          → CompletableFuture.anyOf(...)
///   CancellationToken     → java.util.concurrent.Future.cancel() / Thread.interrupt()
/// </summary>
[ApiController]
[Route("concurrency-demo")]
public class ConcurrencyDemoController(
    ILogger<ConcurrencyDemoController> logger,
    IUnitOfWork                        uow) : ControllerBase
{
    // ── GET /concurrency-demo/accounts/{id} ───────────────────────────────────
    // Standard async/await DB call. CancellationToken is automatically populated by
    // ASP.NET Core from HttpContext.RequestAborted when bound in the action signature.
    // If the client disconnects, ct is cancelled and the DB query is aborted.
    [HttpGet("accounts/{id:int}")]
    public async Task<IActionResult> GetAccount(int id, CancellationToken ct)
    {
        logger.LogInformation("16-A: GetAccount {Id} (thread {Thread})", id, Environment.CurrentManagedThreadId);

        // ConfigureAwait(false) — best practice in library/service code:
        // avoids capturing the SynchronizationContext.
        // In controllers it has no observable effect in .NET 5+, but is shown as a teaching point.
        var account = await uow.Accounts.GetByIdAsync(id).ConfigureAwait(false);

        if (account is null)
        {
            logger.LogWarning("16-A: Account {Id} not found", id);
            return NotFound();
        }

        return Ok(account);
    }

    // ── GET /concurrency-demo/batch?ids=1,2,3 ────────────────────────────────
    // Task.WhenAll: fetch N accounts concurrently rather than sequentially.
    // Each DB call runs as a separate awaitable unit; all are started before any is awaited.
    //
    // Java parallel:
    //   CompletableFuture<Account>[] futures = ids.stream()
    //       .map(id -> CompletableFuture.supplyAsync(() -> repo.findById(id)))
    //       .toArray(CompletableFuture[]::new);
    //   CompletableFuture.allOf(futures).join();
    [HttpGet("batch")]
    public async Task<IActionResult> BatchFetch([FromQuery] string ids, CancellationToken ct)
    {
        var idList = ids.Split(',')
                        .Select(s => int.TryParse(s.Trim(), out var n) ? n : -1)
                        .Where(n => n > 0)
                        .ToList();

        if (idList.Count == 0)
            return BadRequest(new { error = "Provide at least one valid integer id, e.g. ?ids=1,2,3" });

        logger.LogInformation("16-A: WhenAll — fetching {Count} accounts concurrently", idList.Count);

        // Start all tasks before awaiting — this is the fan-out pattern.
        var tasks = idList.Select(id => uow.Accounts.GetByIdAsync(id)).ToList();
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);

        var found   = results.Where(a => a is not null).ToList();
        var missing = idList.Except(found.Select(a => a!.Id)).ToList();

        return Ok(new { found, missing });
    }

    // ── GET /concurrency-demo/fastest?ids=1,2,3 ──────────────────────────────
    // Task.WhenAny: return as soon as the *first* task completes.
    // Useful as a timeout race or "first-result-wins" cache strategy.
    //
    // Java parallel:
    //   CompletableFuture.anyOf(futures).thenApply(result -> (Account) result).join();
    [HttpGet("fastest")]
    public async Task<IActionResult> FastestFetch([FromQuery] string ids, CancellationToken ct)
    {
        var idList = ids.Split(',')
                        .Select(s => int.TryParse(s.Trim(), out var n) ? n : -1)
                        .Where(n => n > 0)
                        .ToList();

        if (idList.Count == 0)
            return BadRequest(new { error = "Provide at least one valid integer id" });

        var tasks = idList.Select(id => uow.Accounts.GetByIdAsync(id)).ToList();
        var first = await Task.WhenAny(tasks).ConfigureAwait(false);
        var result = await first;

        logger.LogInformation("16-A: WhenAny — first result received");

        if (result is null)
            return NotFound();

        return Ok(new { first = result, note = "First of the concurrent fetches to complete" });
    }

    // ── GET /concurrency-demo/with-timeout/{id}?timeoutMs=200 ─────────────────
    // Timeout race: WhenAny between the real work and a Task.Delay acting as a deadline.
    // CancellationTokenSource with a deadline is the idiomatic .NET approach.
    //
    // Java parallel:
    //   future.get(200, TimeUnit.MILLISECONDS) — throws TimeoutException
    [HttpGet("with-timeout/{id:int}")]
    public async Task<IActionResult> GetWithTimeout(int id, [FromQuery] int timeoutMs = 500)
    {
        // Create a linked CTS: cancels if either the HTTP request is aborted OR the deadline fires.
        using var timeoutCts = new CancellationTokenSource(timeoutMs);
        using var linkedCts  = CancellationTokenSource.CreateLinkedTokenSource(
                                   HttpContext.RequestAborted, timeoutCts.Token);

        try
        {
            // NOTE: GetByIdAsync doesn't accept a CancellationToken in this codebase,
            // so we use WhenAny with a delay as a teaching example of the pattern.
            var fetchTask   = uow.Accounts.GetByIdAsync(id);
            var timeoutTask = Task.Delay(timeoutMs, linkedCts.Token);

            var winner = await Task.WhenAny(fetchTask, timeoutTask).ConfigureAwait(false);

            if (winner == timeoutTask)
            {
                logger.LogWarning("16-A: Timeout fetching account {Id} after {Ms}ms", id, timeoutMs);
                return StatusCode(504, new { error = $"Timed out after {timeoutMs}ms" });
            }

            var account = await fetchTask;
            if (account is null) return NotFound();

            logger.LogInformation("16-A: Account {Id} fetched within timeout", id);
            return Ok(account);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("16-A: Request cancelled while fetching account {Id}", id);
            return StatusCode(499, new { error = "Request cancelled" });
        }
    }

    // ── POST /concurrency-demo/cpu-work ───────────────────────────────────────
    // Task.Run — offload a CPU-bound computation so it doesn't block the IO thread.
    // In ASP.NET Core you almost never need this for IO work (which is already async),
    // but it is essential for genuinely CPU-bound operations (e.g. PDF generation, crypto).
    //
    // Java parallel:
    //   CompletableFuture.supplyAsync(() -> heavyComputation(), forkJoinPool)
    [HttpPost("cpu-work")]
    public async Task<IActionResult> CpuWork([FromBody] CpuWorkRequest req)
    {
        logger.LogInformation("16-A: Task.Run offloading CPU work for {N} iterations", req.Iterations);

        // Task.Run schedules work on the ThreadPool, freeing the current IO thread.
        var result = await Task.Run(() =>
        {
            // Simulate a CPU-bound calculation (e.g. interest projection over many accounts)
            long sum = 0;
            for (var i = 0; i < req.Iterations; i++)
                sum += i;
            return sum;
        });

        return Ok(new { iterations = req.Iterations, result });
    }
}

public record CpuWorkRequest(int Iterations);
