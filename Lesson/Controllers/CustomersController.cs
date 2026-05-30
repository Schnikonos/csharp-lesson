using Lesson.Entities;
using Lesson.Repositories;
using Lesson.UnitOfWork;
using Microsoft.AspNetCore.Mvc;

namespace Lesson.Controllers;

/// <summary>
/// Lesson 04-A — demonstrates navigation properties and eager loading.
///
/// KEY PATTERNS
/// ────────────
/// GET /customers/{id}/accounts
///   Loads the customer with ALL accounts via Include + ThenInclude.
///   Without Include, the Accounts collection would be empty even though
///   FK rows exist — EF Core does NOT lazy-load by default.
///
/// GET /customers/{id}/accounts/active
///   Uses a filtered Include so only IsActive = true accounts are returned.
///   The SQL WHERE is applied on the database side — not in C# memory.
///
/// POST /customers/{id}/accounts
///   Assigns a new account to an existing customer by setting the FK.
///   EF Core picks up the FK value from the navigation property automatically.
///
/// Java parallels:
///   @EntityGraph(attributePaths = "accounts") → Include(c => c.Accounts)
///   @Query("... JOIN FETCH a WHERE a.isActive = true") → filtered Include
/// </summary>
[ApiController]
[Route("customers")]
public class CustomersController(ICustomerRepository customers, IUnitOfWork uow) : ControllerBase
{
    // ── GET /customers ────────────────────────────────────────────────────────
    [HttpGet]
    public async Task<ActionResult<IEnumerable<CustomerResponse>>> GetAll()
    {
        var list = await customers.GetAllAsync();
        return Ok(list.Select(c => ToResponse(c)));
    }

    // ── GET /customers/{id}/accounts (Include + ThenInclude) ─────────────────
    [HttpGet("{id:int}/accounts")]
    public async Task<ActionResult<CustomerResponse>> GetWithAccounts(int id)
    {
        var customer = await customers.GetByIdWithAccountsAsync(id);
        if (customer is null)
            return NotFound(new { Error = $"Customer {id} not found." });

        return Ok(ToResponse(customer));
    }

    // ── GET /customers/{id}/accounts/active (filtered Include) ───────────────
    [HttpGet("{id:int}/accounts/active")]
    public async Task<ActionResult<CustomerResponse>> GetWithActiveAccounts(int id)
    {
        var customer = await customers.GetByIdWithActiveAccountsAsync(id);
        if (customer is null)
            return NotFound(new { Error = $"Customer {id} not found." });

        return Ok(ToResponse(customer));
    }

    // ── POST /customers ───────────────────────────────────────────────────────
    [HttpPost]
    public async Task<ActionResult<CustomerResponse>> Create(CreateCustomerRequest request)
    {
        if (await customers.ExistsAsync(request.Email))
            return Conflict(new { Error = $"Email '{request.Email}' already registered." });

        var customer = new Customer { Name = request.Name, Email = request.Email };
        await customers.AddAsync(customer);
        await uow.CommitAsync();

        return CreatedAtAction(nameof(GetWithAccounts), new { id = customer.Id }, ToResponse(customer));
    }

    // ── POST /customers/{id}/accounts ─────────────────────────────────────────
    /// <summary>
    /// Assigns an existing BankAccount to a Customer by setting the FK.
    /// Demonstrates how navigation properties and FK values stay in sync.
    /// </summary>
    [HttpPost("{customerId:int}/accounts/{accountId:int}")]
    public async Task<IActionResult> AssignAccount(int customerId, int accountId)
    {
        var customer = await customers.GetByIdWithAccountsAsync(customerId);
        if (customer is null)
            return NotFound(new { Error = $"Customer {customerId} not found." });

        var account = await uow.Accounts.GetByIdAsync(accountId);
        if (account is null)
            return NotFound(new { Error = $"Account {accountId} not found." });

        // Set FK — EF Core will UPDATE BankAccounts SET CustomerId = @id WHERE Id = @accountId
        account.CustomerId = customerId;
        await uow.CommitAsync();

        return Ok(new { Message = $"Account {accountId} assigned to customer {customerId}." });
    }

    // ── Mapping ───────────────────────────────────────────────────────────────
    private static CustomerResponse ToResponse(Customer c) => new(
        c.Id, c.Name, c.Email,
        c.Accounts.Select(a => new AccountResponse(
            a.Id, a.AccountNumber, a.OwnerName, a.AccountType, a.Balance, a.IsActive,
            a.CreatedAt, a.UpdatedAt, a.UpdatedBy,
            a.Address is null ? null : new AddressDto(
                a.Address.Street, a.Address.City, a.Address.PostalCode, a.Address.Country)))
        .ToList());
}
