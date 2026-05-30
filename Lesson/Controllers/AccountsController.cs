using Lesson.Entities;
using Lesson.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace Lesson.Controllers;

/// <summary>
/// Lesson 03-B — refactored to use IAccountRepository.
///
/// The controller no longer depends on BankingDbContext directly.
/// It talks to IAccountRepository, which hides EF Core details.
/// This makes the controller unit-testable by swapping in a fake repository.
///
/// New features vs 03-A:
///   - GET /accounts?type=Checking  — IQueryable filter delegated to repository
///   - Address (owned entity) included in create / update / response
///
/// Java parallel:
///   @RestController + @RequestMapping("/accounts")
///   @Autowired AccountRepository repo  →  injected IAccountRepository
/// </summary>
[ApiController]
[Route("accounts")]
public class AccountsController(IAccountRepository repo) : ControllerBase
{
    // -------------------------------------------------------------------------
    // GET /accounts?type={accountType}
    // Java: @GetMapping / repository.findAll() or repository.findByAccountType(type)
    // -------------------------------------------------------------------------
    [HttpGet]
    public async Task<ActionResult<IEnumerable<AccountResponse>>> GetAll(
        [FromQuery] string? type = null)
    {
        var accounts = await repo.GetAllAsync(type);
        return Ok(accounts.Select(ToResponse));
    }

    // -------------------------------------------------------------------------
    // GET /accounts/{id}
    // Java: @GetMapping("/{id}") / repository.findById(id)
    // -------------------------------------------------------------------------
    [HttpGet("{id:int}")]
    public async Task<ActionResult<AccountResponse>> GetById(int id)
    {
        var account = await repo.GetByIdAsync(id);
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
        if (await repo.ExistsAsync(request.AccountNumber))
            return Conflict(new { Error = $"Account number '{request.AccountNumber}' already exists." });

        var account = new BankAccount
        {
            AccountNumber = request.AccountNumber,
            OwnerName     = request.OwnerName,
            AccountType   = request.AccountType,
            Balance       = request.InitialBalance,
            IsActive      = true,
            CreatedAt     = DateTime.UtcNow,
            Address       = ToAddress(request.Address)
        };

        await repo.AddAsync(account);

        return CreatedAtAction(nameof(GetById), new { id = account.Id }, ToResponse(account));
    }

    // -------------------------------------------------------------------------
    // PUT /accounts/{id}
    // Java: @PutMapping("/{id}") / repository.save(existingEntity)
    // -------------------------------------------------------------------------
    [HttpPut("{id:int}")]
    public async Task<ActionResult<AccountResponse>> Update(int id, UpdateAccountRequest request)
    {
        var account = await repo.GetByIdAsync(id);
        if (account is null)
            return NotFound(new { Error = $"Account {id} not found." });

        account.OwnerName   = request.OwnerName;
        account.AccountType = request.AccountType;
        account.Balance     = request.Balance;
        account.IsActive    = request.IsActive;
        account.Address     = ToAddress(request.Address);

        await repo.UpdateAsync(account);

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
        var account = await repo.GetByIdAsync(id);
        if (account is null)
            return NotFound(new { Error = $"Account {id} not found." });

        await repo.DeleteAsync(account);

        return NoContent();
    }

    // ── Mapping helpers ───────────────────────────────────────────────────────
    private static AccountResponse ToResponse(BankAccount a) => new(
        a.Id, a.AccountNumber, a.OwnerName, a.AccountType, a.Balance, a.IsActive, a.CreatedAt,
        a.Address is null ? null : new AddressDto(
            a.Address.Street, a.Address.City, a.Address.PostalCode, a.Address.Country));

    private static Entities.Address? ToAddress(AddressDto? dto) =>
        dto is null ? null : new Entities.Address
        {
            Street     = dto.Street,
            City       = dto.City,
            PostalCode = dto.PostalCode,
            Country    = dto.Country
        };
}
