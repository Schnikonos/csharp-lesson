using Lesson.Data;
using Lesson.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Lesson.Controllers;

/// <summary>
/// Lesson 03-A — Full CRUD over BankAccount via EF Core.
///
/// Endpoints:
///   GET    /accounts          — list all accounts
///   GET    /accounts/{id}     — get by primary key
///   POST   /accounts          — create new account
///   PUT    /accounts/{id}     — full update
///   DELETE /accounts/{id}     — hard delete (soft delete in 03-C)
///
/// Java parallel:
///   @RestController + @RequestMapping("/accounts")
///   @Autowired AccountRepository  →  injected BankingDbContext
///   repository.findAll()          →  dbContext.BankAccounts.ToListAsync()
///   repository.findById(id)       →  dbContext.BankAccounts.FindAsync(id)
///   repository.save(entity)       →  dbContext.Add(entity) + SaveChangesAsync()
///   repository.delete(entity)     →  dbContext.Remove(entity) + SaveChangesAsync()
/// </summary>
[ApiController]
[Route("accounts")]
public class AccountsController(BankingDbContext db) : ControllerBase
{
    // -------------------------------------------------------------------------
    // GET /accounts
    // Java: @GetMapping / repository.findAll()
    // -------------------------------------------------------------------------
    [HttpGet]
    public async Task<ActionResult<IEnumerable<AccountResponse>>> GetAll()
    {
        var accounts = await db.BankAccounts
            .OrderBy(a => a.Id)
            .Select(a => ToResponse(a))
            .ToListAsync();

        return Ok(accounts);
    }

    // -------------------------------------------------------------------------
    // GET /accounts/{id}
    // Java: @GetMapping("/{id}") / repository.findById(id)
    // -------------------------------------------------------------------------
    [HttpGet("{id:int}")]
    public async Task<ActionResult<AccountResponse>> GetById(int id)
    {
        // FindAsync looks up by PK — uses identity cache first, then DB.
        var account = await db.BankAccounts.FindAsync(id);
        if (account is null)
            return NotFound(new { Error = $"Account {id} not found." });

        return Ok(ToResponse(account));
    }

    // -------------------------------------------------------------------------
    // POST /accounts
    // Java: @PostMapping / repository.save(newEntity)
    // -------------------------------------------------------------------------
    [HttpPost]
    public async Task<ActionResult<AccountResponse>> Create(CreateAccountRequest request)
    {
        // Check for duplicate account number
        if (await db.BankAccounts.AnyAsync(a => a.AccountNumber == request.AccountNumber))
            return Conflict(new { Error = $"Account number '{request.AccountNumber}' already exists." });

        var account = new BankAccount
        {
            AccountNumber = request.AccountNumber,
            OwnerName     = request.OwnerName,
            AccountType   = request.AccountType,
            Balance       = request.InitialBalance,
            IsActive      = true,
            CreatedAt     = DateTime.UtcNow
        };

        // Add tracks the entity as Added; SaveChangesAsync issues INSERT + returns the generated Id.
        db.BankAccounts.Add(account);
        await db.SaveChangesAsync();

        // 201 Created with Location header pointing to the new resource.
        return CreatedAtAction(nameof(GetById), new { id = account.Id }, ToResponse(account));
    }

    // -------------------------------------------------------------------------
    // PUT /accounts/{id}
    // Full replacement update (PATCH / partial update is in 03-B).
    // Java: @PutMapping("/{id}") / repository.save(existingEntity)
    // -------------------------------------------------------------------------
    [HttpPut("{id:int}")]
    public async Task<ActionResult<AccountResponse>> Update(int id, UpdateAccountRequest request)
    {
        var account = await db.BankAccounts.FindAsync(id);
        if (account is null)
            return NotFound(new { Error = $"Account {id} not found." });

        // EF Core tracks all scalar property changes automatically.
        // Calling SaveChangesAsync() issues UPDATE only for changed columns.
        account.OwnerName   = request.OwnerName;
        account.AccountType = request.AccountType;
        account.Balance     = request.Balance;
        account.IsActive    = request.IsActive;

        await db.SaveChangesAsync();

        return Ok(ToResponse(account));
    }

    // -------------------------------------------------------------------------
    // DELETE /accounts/{id}
    // Hard delete — soft delete with global query filters in 03-C.
    // Java: @DeleteMapping("/{id}") / repository.deleteById(id)
    // -------------------------------------------------------------------------
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var account = await db.BankAccounts.FindAsync(id);
        if (account is null)
            return NotFound(new { Error = $"Account {id} not found." });

        db.BankAccounts.Remove(account);
        await db.SaveChangesAsync();

        return NoContent();
    }

    // ── Mapping helper ────────────────────────────────────────────────────────
    private static AccountResponse ToResponse(BankAccount a) => new(
        a.Id, a.AccountNumber, a.OwnerName, a.AccountType, a.Balance, a.IsActive, a.CreatedAt);
}
