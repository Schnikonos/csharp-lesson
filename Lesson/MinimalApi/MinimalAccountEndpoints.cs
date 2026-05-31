using FluentValidation;
using Lesson.Data;
using Lesson.Entities;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Lesson.MinimalApi;

/// <summary>
/// Lesson 21-A — Minimal API endpoints for account management.
///
/// Minimal APIs skip controller classes; routes are defined directly on the
/// WebApplication/RouteGroupBuilder.  IEndpointFilter provides cross-cutting
/// logic (validation, correlation) without action filters.
///
/// Java parallel:
///   Spring @RequestMapping methods on @RestController  →  MapGet/MapPost lambda
///   @RestControllerAdvice / HandlerInterceptor         →  IEndpointFilter
///   TypedResults.Ok / NotFound                         →  ResponseEntity.ok() / notFound()
/// </summary>
public static class MinimalAccountEndpoints
{
    public static RouteGroupBuilder MapMinimalAccounts(this IEndpointRouteBuilder app)
    {
        // Group all routes under /minimal/accounts; apply the validation filter globally
        var group = app
            .MapGroup("/minimal/accounts")
            .AddEndpointFilter<ValidationEndpointFilter>()
            .WithTags("MinimalAccounts");

        // GET /minimal/accounts — list all (no soft-delete filter to keep example clean)
        group.MapGet("/", async (BankingDbContext db, CancellationToken ct) =>
        {
            var accounts = await db.BankAccounts
                .Select(a => new AccountDto(a.Id, a.AccountNumber, a.OwnerName, a.Balance))
                .ToListAsync(ct);
            return TypedResults.Ok(accounts);
        });

        // GET /minimal/accounts/{id}
        group.MapGet("/{id:int}", async (int id, BankingDbContext db, CancellationToken ct) =>
        {
            var a = await db.BankAccounts.FindAsync([id], ct);
            return a is null
                ? Results.NotFound(new { id })
                : Results.Ok(new AccountDto(a.Id, a.AccountNumber, a.OwnerName, a.Balance));
        });

        // POST /minimal/accounts — create account; ValidationEndpointFilter runs first
        group.MapPost("/", async (
            CreateMinimalAccountRequest req,
            BankingDbContext db,
            CancellationToken ct) =>
        {
            var account = new BankAccount
            {
                AccountNumber = req.AccountNumber,
                OwnerName     = req.OwnerName,
                Balance       = req.InitialBalance,
                AccountType   = "Savings",
            };
            db.BankAccounts.Add(account);
            await db.SaveChangesAsync(ct);

            return TypedResults.Created(
                $"/minimal/accounts/{account.Id}",
                new AccountDto(account.Id, account.AccountNumber, account.OwnerName, account.Balance));
        });

        // DELETE /minimal/accounts/{id} — soft-delete
        group.MapDelete("/{id:int}", async (int id, BankingDbContext db, CancellationToken ct) =>
        {
            var a = await db.BankAccounts.FindAsync([id], ct);
            if (a is null) return Results.NotFound(new { id });
            a.IsDeleted = true;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        return group;
    }
}

// ── Request / response DTOs ────────────────────────────────────────────────
public record AccountDto(int Id, string AccountNumber, string OwnerName, decimal Balance);

public record CreateMinimalAccountRequest(string AccountNumber, string OwnerName, decimal InitialBalance);

// ── FluentValidation validator ──────────────────────────────────────────────
public class CreateMinimalAccountRequestValidator : AbstractValidator<CreateMinimalAccountRequest>
{
    public CreateMinimalAccountRequestValidator()
    {
        RuleFor(x => x.AccountNumber).NotEmpty().MaximumLength(30);
        RuleFor(x => x.OwnerName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.InitialBalance).GreaterThanOrEqualTo(0);
    }
}

/// <summary>
/// Lesson 21-A — IEndpointFilter for input validation.
///
/// Runs before the endpoint handler; if the first argument is a request DTO that
/// has a registered IValidator&lt;T&gt;, it validates and returns 400 on failure.
///
/// Java parallel:
///   @Valid + BindingResult / @RequestBody validated by Bean Validation
///   Spring HandlerInterceptor.preHandle → IEndpointFilter.InvokeAsync
/// </summary>
public class ValidationEndpointFilter(IServiceProvider sp) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        // Find the first argument that has a registered validator
        foreach (var arg in ctx.Arguments)
        {
            if (arg is null) continue;
            var validatorType = typeof(IValidator<>).MakeGenericType(arg.GetType());
            if (sp.GetService(validatorType) is IValidator validator)
            {
                var result = await validator.ValidateAsync(new ValidationContext<object>(arg));
                if (!result.IsValid)
                    return Results.ValidationProblem(result.ToDictionary());
            }
        }
        return await next(ctx);
    }
}
