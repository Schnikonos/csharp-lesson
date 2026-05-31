using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using Lesson.Entities;
using Lesson.UnitOfWork;

namespace Lesson.Controllers;

/// <summary>
/// Lesson 14-B — IDistributedCache + Redis: cache-aside with serialization.
///
/// IDistributedCache is the interface; the backing store is pluggable:
///   • Development: AddDistributedMemoryCache()  (in-process, single-node)
///   • Production:  AddStackExchangeRedisCache()  (external, multi-node)
///
/// Because IDistributedCache only stores byte[], we must serialize/deserialize
/// manually (System.Text.Json used here).
///
/// Java parallel:
///   Spring @Cacheable + RedisCacheManager  → IDistributedCache + Redis provider
///   RedisTemplate<String, Object>           → IDistributedCache + JsonSerializer
/// </summary>
[ApiController]
[Route("distributed-cache")]
public class DistributedCacheController(
    IDistributedCache cache,
    IUnitOfWork       uow) : ControllerBase
{
    private static string AccountKey(int id) => $"dist:account:{id}";

    // ── GET /distributed-cache/accounts/{id} ─────────────────────────────────
    [HttpGet("accounts/{id:int}")]
    public async Task<IActionResult> GetAccount(int id)
    {
        // Step 1 — try cache
        var bytes = await cache.GetAsync(AccountKey(id));
        if (bytes is not null)
        {
            var cached = Deserialize<BankAccount>(bytes);
            return cached is null ? NotFound() : Ok(new { source = "cache", cached });
        }

        // Step 2 — cache miss: load from DB
        var account = await uow.Accounts.GetByIdAsync(id);
        if (account is null) return NotFound();

        // Step 3 — store in cache
        await cache.SetAsync(
            AccountKey(id),
            Serialize(account),
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),
                SlidingExpiration               = TimeSpan.FromMinutes(2),
            });

        return Ok(new { source = "db", account });
    }

    // ── POST /distributed-cache/accounts — create + invalidate ───────────────
    [HttpPost("accounts")]
    public async Task<IActionResult> Create([FromBody] CreateAccountRequest request)
    {
        var newAccount = new BankAccount
        {
            AccountNumber = request.AccountNumber,
            OwnerName     = request.OwnerName,
            AccountType   = request.AccountType,
            Balance       = request.InitialBalance,
            IsActive      = true
        };
        await uow.Accounts.AddAsync(newAccount);
        await uow.CommitAsync();

        // Invalidate — forces next GET to repopulate from DB
        await cache.RemoveAsync(AccountKey(newAccount.Id));

        return CreatedAtAction(nameof(GetAccount), new { id = newAccount.Id }, newAccount);
    }

    // ── DELETE /distributed-cache/accounts/{id}/cache ────────────────────────
    [HttpDelete("accounts/{id:int}/cache")]
    public async Task<IActionResult> Evict(int id)
    {
        await cache.RemoveAsync(AccountKey(id));
        return NoContent();
    }

    // ── Serialization helpers ─────────────────────────────────────────────────
    private static byte[] Serialize<T>(T value) =>
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value));

    private static T? Deserialize<T>(byte[] bytes) =>
        JsonSerializer.Deserialize<T>(Encoding.UTF8.GetString(bytes));
}
