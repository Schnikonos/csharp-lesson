using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Lesson.Entities;
using Lesson.UnitOfWork;

namespace Lesson.Controllers;

/// <summary>
/// Lesson 14-A — IMemoryCache: Set, Get, GetOrCreate, absolute and sliding expiry.
///
/// Demonstrates the cache-aside pattern:
///   1. Check cache first
///   2. On miss: load from DB / service, store in cache, return
///   3. On write: invalidate (or update) the cache entry
///
/// Java parallel:
///   Spring @Cacheable / @CacheEvict / @CachePut  → IMemoryCache manual API
///   @EnableCaching                                → AddMemoryCache() in DI
///   CacheManager                                  → IMemoryCache
/// </summary>
[ApiController]
[Route("cache-demo")]
public class MemoryCacheController(
    IMemoryCache cache,
    IUnitOfWork  uow) : ControllerBase
{
    private static string AccountKey(int id) => $"account:{id}";
    private static readonly string AllAccountsKey = "accounts:all";

    // ── GET /cache-demo/accounts/{id} — cache-aside pattern ──────────────────
    [HttpGet("accounts/{id:int}")]
    public async Task<IActionResult> GetAccount(int id)
    {
        // GetOrCreateAsync: returns from cache if present; otherwise calls factory
        // Java: @Cacheable(value = "accounts", key = "#id")
        var account = await cache.GetOrCreateAsync(
            AccountKey(id),
            async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
                entry.SlidingExpiration               = TimeSpan.FromMinutes(2);
                return await uow.Accounts.GetByIdAsync(id);
            });

        return account is null ? NotFound() : Ok(account);
    }

    // ── GET /cache-demo/accounts — cache the full list ────────────────────────
    [HttpGet("accounts")]
    public async Task<IActionResult> GetAll()
    {
        if (!cache.TryGetValue(AllAccountsKey, out IEnumerable<BankAccount>? list))
        {
            list = await uow.Accounts.GetAllAsync(null);
            var opts = new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(TimeSpan.FromMinutes(1))
                .SetPriority(CacheItemPriority.Normal);
            cache.Set(AllAccountsKey, list, opts);
        }
        return Ok(list);
    }

    // ── POST /cache-demo/accounts — invalidate on write ───────────────────────
    // Java: @CacheEvict(value = "accounts", allEntries = true)
    [HttpPost("accounts")]
    public async Task<IActionResult> CreateAndInvalidate(
        [FromBody] CreateAccountRequest request)
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
        await uow.CommitAsync();               // persists to DB before we return the ID

        cache.Remove(AllAccountsKey);

        return CreatedAtAction(nameof(GetAccount),
            new { id = newAccount.Id }, newAccount);
    }

    // ── DELETE /cache-demo/accounts/{id}/cache — manual eviction demo ─────────
    [HttpDelete("accounts/{id:int}/cache")]
    public IActionResult Evict(int id)
    {
        cache.Remove(AccountKey(id));
        cache.Remove(AllAccountsKey);
        return NoContent();
    }
}
