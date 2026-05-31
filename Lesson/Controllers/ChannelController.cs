using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.AspNetCore.Mvc;
using Lesson.UnitOfWork;

namespace Lesson.Controllers;

/// <summary>
/// Lesson 16-C — Advanced concurrency: Channel&lt;T&gt;, IAsyncEnumerable&lt;T&gt;,
///               ValueTask, ReaderWriterLockSlim, Mutex.
///
/// Key concepts:
///   1. Channel&lt;T&gt;              — the modern producer/consumer queue in .NET.
///                                Replaces BlockingCollection for async scenarios.
///   2. IAsyncEnumerable&lt;T&gt;    — async streaming: yield results as they are produced
///                                rather than buffering everything before returning.
///   3. ValueTask              — allocation-free alternative to Task for hot paths
///                                that frequently complete synchronously.
///   4. ReaderWriterLockSlim   — allow concurrent reads, exclusive writes.
///   5. Mutex                  — named, cross-process mutual exclusion (OS primitive).
///
/// Java parallels:
///   Channel&lt;T&gt;               → BlockingQueue / LinkedTransferQueue
///   IAsyncEnumerable&lt;T&gt;     → java.util.stream.Stream (lazy) / RxJava Observable
///   ValueTask                → there is no direct equivalent; CompletableFuture always allocates
///   ReaderWriterLockSlim     → ReentrantReadWriteLock
///   Mutex                    → java.util.concurrent.locks.Lock / OS mutex via JNI
/// </summary>
[ApiController]
[Route("advanced-concurrency")]
public class ChannelController(
    ILogger<ChannelController> logger,
    IUnitOfWork                uow,
    IServiceScopeFactory       scopeFactory) : ControllerBase
{
    // ── Singleton Channel used as a shared work queue ─────────────────────────
    // BoundedChannel caps the buffer — back-pressure if producer is faster than consumer.
    // Java parallel: new ArrayBlockingQueue<>(100)
    private static readonly Channel<int> _workQueue =
        Channel.CreateBounded<int>(new BoundedChannelOptions(100)
        {
            FullMode    = BoundedChannelFullMode.Wait,  // producer awaits when full
            SingleReader = false,
            SingleWriter = false,
        });

    // ── ReaderWriterLockSlim — reads can overlap, writes are exclusive ─────────
    // Java parallel: ReentrantReadWriteLock rw = new ReentrantReadWriteLock();
    //                rw.readLock().lock(); ... rw.readLock().unlock();
    private static readonly ReaderWriterLockSlim _rwLock = new();
    private static readonly HashSet<int> _flaggedAccounts = new();

    // ── POST /advanced-concurrency/enqueue ────────────────────────────────────
    // Producer: writes account IDs to the channel for background processing.
    [HttpPost("enqueue")]
    public async Task<IActionResult> Enqueue([FromBody] EnqueueRequest req)
    {
        foreach (var id in req.AccountIds)
        {
            // WriteAsync back-pressures if the bounded channel is full
            // Java parallel: queue.put(id); (blocks if full)
            await _workQueue.Writer.WriteAsync(id, HttpContext.RequestAborted);
            logger.LogInformation("16-C: Enqueued account {Id}", id);
        }
        return Accepted(new { queued = req.AccountIds.Length });
    }

    // ── GET /advanced-concurrency/drain ───────────────────────────────────────
    // Consumer: drains available items from the channel (without waiting for new ones).
    [HttpGet("drain")]
    public IActionResult Drain()
    {
        var drained = new List<int>();
        // TryRead is non-blocking; ReadAllAsync(ct) would block until the channel is closed.
        while (_workQueue.Reader.TryRead(out var id))
            drained.Add(id);

        logger.LogInformation("16-C: Drained {Count} items from channel", drained.Count);
        return Ok(new { drained });
    }

    // ── GET /advanced-concurrency/stream?ids=1,2,3 ────────────────────────────
    // IAsyncEnumerable streaming: yield each result as it is produced.
    // The client receives a JSON array streamed line-by-line (via ASP.NET Core's
    // built-in support for IAsyncEnumerable return types in controllers).
    //
    // Java parallel:
    //   @GetMapping(produces = MediaType.TEXT_EVENT_STREAM_VALUE)
    //   public Flux<Account> stream() { return accountFlux; }  (Project Reactor)
    [HttpGet("stream")]
    public IAsyncEnumerable<object> StreamAccounts(
        [FromQuery] string ids,
        CancellationToken ct)
    {
        var idList = ids.Split(',')
                        .Select(s => int.TryParse(s.Trim(), out var n) ? n : -1)
                        .Where(n => n > 0)
                        .ToList();

        return StreamAccountsCore(idList, ct);
    }

    // Private async iterator — the compiler generates an IAsyncEnumerable state machine.
    // Java parallel: a Flux or Stream pipeline that lazily emits elements
    private async IAsyncEnumerable<object> StreamAccountsCore(
        List<int> ids,
        [EnumeratorCancellation] CancellationToken ct)
    {
        foreach (var id in ids)
        {
            ct.ThrowIfCancellationRequested();

            await using var scope = scopeFactory.CreateAsyncScope();
            var scopedUow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var account   = await scopedUow.Accounts.GetByIdAsync(id);

            logger.LogInformation("16-C: Streaming account {Id}", id);

            yield return account is null
                ? new { id, status = "not found" }
                : new { id, account.Balance, status = "ok" };
        }
    }

    // ── GET /advanced-concurrency/valuetask/{id} ──────────────────────────────
    // ValueTask: avoids a heap allocation for the common synchronous-cache-hit path.
    // Java parallel: no direct equivalent — CompletableFuture.completedFuture(v)
    //                allocates an object; ValueTask<T> on a sync path allocates nothing.
    [HttpGet("valuetask/{id:int}")]
    public async Task<IActionResult> GetWithValueTask(int id)
    {
        var balance = await GetCachedBalanceAsync(id);
        if (balance is null) return NotFound();
        return Ok(new { id, balance });
    }

    // ValueTask<T> is ideal for a cache-first lookup:
    //   - Cache hit  → returns synchronously, zero allocation.
    //   - Cache miss → falls back to async DB call.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, decimal> _vtCache = new();

    private async ValueTask<decimal?> GetCachedBalanceAsync(int id)
    {
        if (_vtCache.TryGetValue(id, out var cached))
            return cached; // synchronous path — no Task allocation

        // Cache miss: async IO
        await using var scope = scopeFactory.CreateAsyncScope();
        var scopedUow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var account   = await scopedUow.Accounts.GetByIdAsync(id);
        if (account is null) return null;

        _vtCache.TryAdd(id, account.Balance);
        return account.Balance;
    }

    // ── POST /advanced-concurrency/flag/{id} ──────────────────────────────────
    // ReaderWriterLockSlim: write lock for mutations; many concurrent readers.
    [HttpPost("flag/{id:int}")]
    public IActionResult FlagAccount(int id)
    {
        _rwLock.EnterWriteLock();
        try
        {
            _flaggedAccounts.Add(id);
            logger.LogInformation("16-C: Flagged account {Id} (write lock held)", id);
        }
        finally { _rwLock.ExitWriteLock(); }

        return Ok(new { flagged = id });
    }

    // ── GET /advanced-concurrency/flagged ─────────────────────────────────────
    // Read lock: multiple concurrent readers, no writer.
    [HttpGet("flagged")]
    public IActionResult GetFlagged()
    {
        _rwLock.EnterReadLock();
        try
        {
            var snapshot = _flaggedAccounts.ToArray();
            logger.LogInformation("16-C: Read flagged accounts (read lock held), count={Count}", snapshot.Length);
            return Ok(new { flagged = snapshot });
        }
        finally { _rwLock.ExitReadLock(); }
    }
}

public record EnqueueRequest(int[] AccountIds);
