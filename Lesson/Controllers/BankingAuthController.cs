using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lesson.Controllers;

/// <summary>
/// Lesson 13-B — Role-based and claim-based authorization.
///
/// Three access levels:
///   GET /banking/balance/{accountId}  — any authenticated user
///   POST /banking/transfer            — Teller or Manager only  [Authorize(Roles = "Teller,Manager")]
///   DELETE /banking/accounts/{id}     — Manager only + custom IAuthorizationRequirement
///
/// IAuthorizationHandler pattern:
///   BankingResource carries the ownerId.
///   AccountOwnerRequirement is satisfied if the caller is a Manager OR owns the resource.
///
/// Java parallel:
///   [Authorize(Roles = "Teller")]  → @PreAuthorize("hasRole('TELLER')")
///   IAuthorizationHandler          → @PreAuthorize("@accountSecurity.canAccess(#id)")
///                                    implemented as a Spring @Component
/// </summary>
[ApiController]
[Route("banking")]
[Authorize]                         // all actions require authentication by default
public class BankingAuthController : ControllerBase
{
    private readonly IAuthorizationService _authz;
    public BankingAuthController(IAuthorizationService authz) => _authz = authz;

    // ── Any authenticated user ────────────────────────────────────────────────
    [HttpGet("balance/{accountId:int}")]
    public IActionResult GetBalance(int accountId)
    {
        var caller = User.Identity?.Name;
        return Ok(new { accountId, balance = 9_999.99m, calledBy = caller });
    }

    // ── Role-based authorization ──────────────────────────────────────────────
    // Java: @PreAuthorize("hasAnyRole('TELLER','MANAGER')")
    [HttpPost("transfer")]
    [Authorize(Roles = "Teller,Manager")]
    public IActionResult Transfer([FromBody] TransferRequest request)
        => Ok(new { message = $"Transfer {request.Amount:C} from {request.From} to {request.To} authorised." });

    // ── Custom IAuthorizationRequirement ──────────────────────────────────────
    // Only the account owner OR a Manager may close an account.
    // Java: @PreAuthorize("@bankingSecurity.canClose(authentication, #id)")
    [HttpDelete("accounts/{id:int}")]
    public async Task<IActionResult> CloseAccount(int id)
    {
        var resource = new BankingResource(OwnerId: id.ToString());
        var result   = await _authz.AuthorizeAsync(User, resource, "AccountOwner");
        if (!result.Succeeded)
            return Forbid();

        return Ok(new { message = $"Account {id} closed." });
    }
}

public record TransferRequest(string From, string To, decimal Amount);
public record BankingResource(string OwnerId);

// ─────────────────────────────────────────────────────────────────────────────
// Custom requirement + handler
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Requirement: caller owns the resource (their "sub" claim matches OwnerId)
/// OR is a Manager.
/// </summary>
public class AccountOwnerRequirement : IAuthorizationRequirement { }

public class AccountOwnerHandler : AuthorizationHandler<AccountOwnerRequirement, BankingResource>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext           context,
        AccountOwnerRequirement               requirement,
        BankingResource                       resource)
    {
        // Managers can do everything
        if (context.User.IsInRole("Manager"))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        // Owners can close their own account
        var sub = context.User.FindFirstValue(ClaimTypes.Name);
        if (sub == resource.OwnerId)
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}
