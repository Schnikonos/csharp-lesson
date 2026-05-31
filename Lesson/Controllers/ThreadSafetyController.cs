using System.Collections.Concurrent;
using Microsoft.AspNetCore.Mvc;
using Lesson.UnitOfWork;

namespace Lesson.Controllers;

/// <summary>
/// Lesson 16-B — Thread-safety primitives: lock, Monitor, Interlocked,
///               SemaphoreSlim, ConcurrentDictionary, Parallel.For/ForEach.
///
/// Key concepts demonstrated:
///   1. lock / Monitor   — mutual exclusion for short critical sections.
///   2. Interlocked      — atomic increment/decrement/compare-exchange without a lock.
///   3. SemaphoreSlim    — async-compatible throttle (limit concurrent IO calls).
///   4. ConcurrentDictionary — lock-free thread-safe dictionary.
///   5. Parallel.For/ForEachAsync — data parallelism on CPU-bound work.
///   6. ThreadLocal&lt;T&gt;  — per-thread state (e.g. per-thread counter).
///
/// Java parallels:
///   lock              → synchronized block / ReentrantLock
///   Interlocked       → AtomicInteger / AtomicLong
///   SemaphoreSlim     → Semaphore / java.util.concurrent.Semaphore
///   ConcurrentDictionary → ConcurrentHashMap
///   Parallel.ForEach  → parallelStream().forEach(...)
///   ThreadLocal&lt;T&gt;   → ThreadLocal&lt;T&gt; (identical concept)
/// </summary>
[ApiController]
[Route("thread-safety")]
public class ThreadSafetyController(
    ILogger<ThreadSafetyController> logger,
    IUnitOfWork                     uow,
    IServiceScopeFactory            scopeFactory) : ControllerBase
{
    // ── Shared mutable state (for demonstration) ──────────────────────────────
    // In a real app this state lives in a singleton service, not a controller.
    // The controller is created per-request; these static fields simulate shared state.

    // lock object — never lock on `this` or `typeof(T)` (public objects risk deadlock).
    private static readonly object _lockObj = new();
    private static int _requestCount; // protected by _lockObj

    // Interlocked counter — no lock needed; atomic hardware instruction.
    private static long _atomicCounter;

    // SemaphoreSlim(initialCount, maxCount) — limits concurrent access to a resource.
    // Here we cap at 3 concurrent "slow" operations.
    private static readonly SemaphoreSlim _semaphore = new(3, 3);

    // ConcurrentDictionary — thread-safe, lock-free for most operations.
    private static readonly ConcurrentDictionary<int, decimal> _balanceCache = new();

    // ThreadLocal — each thread sees its own copy; useful for per-thread counters.
    private static readonly ThreadLocal<int> _threadCallCount = new(() => 0);

    // ── GET /thread-safety/stats ───────────────────────────────────────────────
    // Shows all shared counters — demonstrates that lock and Interlocked
    // protect different kinds of shared state.
    [HttpGet("stats")]
    public IActionResult GetStats()
    {
        int snapshot;
        lock (_lockObj)
        {
            _requestCount++;
            snapshot = _requestCount;
        }

        // Interlocked.Increment returns the new value atomically
        // Java parallel: atomicCounter.incrementAndGet()
        var atomic = Interlocked.Increment(ref _atomicCounter);

        logger.LogInformation("16-B: stats — lock-guarded={Lock} atomic={Atomic}", snapshot, atomic);

        return Ok(new
        {
            lockGuardedRequestCount = snapshot,
            atomicCounter           = atomic,
            cacheEntries            = _balanceCache.Count,
            threadId                = Environment.CurrentManagedThreadId,
        });
    }

    // ── GET /thread-safety/semaphore/{id} ────────────────────────────────────
    // SemaphoreSlim throttles how many requests can hit the "slow" DB path concurrently.
    // Pattern: Acquire → do work → Release in finally.
    // Java parallel: semaphore.acquire(); try { ... } finally { semaphore.release(); }
    [HttpGet("semaphore/{id:int}")]
    public async Task<IActionResult> GetWithSemaphore(int id)
    {
        logger.LogInformation("16-B: SemaphoreSlim — waiting (count={Count})", _semaphore.CurrentCount);

        // WaitAsync is the async equivalent of semaphore.acquire() — does not block a thread.
        await _semaphore.WaitAsync(HttpContext.RequestAborted);
        try
        {
            logger.LogInformation("16-B: SemaphoreSlim — acquired, fetching account {Id}", id);
            var account = await uow.Accounts.GetByIdAsync(id);
            if (account is null) return NotFound();

            // Cache balance in ConcurrentDictionary after successful fetch
            // GetOrAdd is atomic: if key exists returns existing value, else calls factory.
            // Java parallel: concurrentHashMap.computeIfAbsent(id, k -> account.getBalance())
            _balanceCache.TryAdd(id, account.Balance);

            return Ok(account);
        }
        finally
        {
            _semaphore.Release();
            logger.LogInformation("16-B: SemaphoreSlim — released");
        }
    }

    // ── GET /thread-safety/cache/{id} ────────────────────────────────────────
    // ConcurrentDictionary read/write without extra locking.
    [HttpGet("cache/{id:int}")]
    public async Task<IActionResult> GetCached(int id)
    {
        // TryGetValue is lock-free and thread-safe
        if (_balanceCache.TryGetValue(id, out var cachedBalance))
        {
            logger.LogInformation("16-B: ConcurrentDictionary hit for account {Id}", id);
            return Ok(new { id, cachedBalance, source = "ConcurrentDictionary" });
        }

        var account = await uow.Accounts.GetByIdAsync(id);
        if (account is null) return NotFound();

        // AddOrUpdate — atomic compare-and-swap under the hood
        // Java parallel: concurrentHashMap.merge(id, balance, (o, n) -> n)
        _balanceCache.AddOrUpdate(id, account.Balance, (_, _) => account.Balance);

        logger.LogInformation("16-B: ConcurrentDictionary miss, fetched account {Id}", id);
        return Ok(new { id, account.Balance, source = "Database" });
    }

    // ── POST /thread-safety/parallel-interest ────────────────────────────────
    // Parallel.ForEachAsync — process N accounts concurrently with a degree-of-parallelism cap.
    // Each iteration is itself async (IO-bound DB fetch).
    // Java parallel: ids.parallelStream().forEach(id -> process(id))
    //                or a CompletableFuture pool with bounded executor
    [HttpPost("parallel-interest")]
    public async Task<IActionResult> ApplyParallelInterest([FromBody] ParallelInterestRequest req)
    {
        var ids = req.AccountIds;
        if (ids is not { Length: > 0 })
            return BadRequest(new { error = "Provide at least one account ID." });

        var results = new ConcurrentBag<string>();

        // ParallelOptions limits the thread pool usage.
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Min(4, ids.Length),
            CancellationToken      = HttpContext.RequestAborted,
        };

        // Each parallel iteration must use its own DI scope because DbContext is NOT thread-safe.
        // Creating a new scope per iteration is the standard pattern for parallel DB work.
        // Java parallel: use separate EntityManager per thread in a parallelStream task.
        await Parallel.ForEachAsync(ids, options, async (id, ct) =>
        {
            await using var scope   = scopeFactory.CreateAsyncScope();
            var scopedUow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var account   = await scopedUow.Accounts.GetByIdAsync(id);
            if (account is null)
            {
                results.Add($"Account {id}: NOT FOUND");
                return;
            }
            var interest = Math.Round(account.Balance * (decimal)(req.AnnualRatePercent / 100.0 / 365.0), 4);
            results.Add($"Account {id}: balance={account.Balance} daily-interest={interest}");
        });

        logger.LogInformation("16-B: Parallel.ForEachAsync processed {Count} accounts", ids.Length);
        return Ok(new { processed = results.OrderBy(s => s).ToList() });
    }

    // ── GET /thread-safety/interlocked-compare ────────────────────────────────
    // CompareExchange — atomically update a value only if it equals the expected value.
    // Java parallel: AtomicLong.compareAndSet(expected, update)
    [HttpGet("interlocked-compare")]
    public IActionResult InterlockedCompareExchange([FromQuery] long expected, [FromQuery] long newValue)
    {
        var before = Interlocked.CompareExchange(ref _atomicCounter, newValue, expected);
        var swapped = before == expected;
        return Ok(new { before, after = _atomicCounter, swapped });
    }
}

public record ParallelInterestRequest(int[] AccountIds, double AnnualRatePercent);
