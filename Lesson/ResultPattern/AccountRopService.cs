using ErrorOr;
using FluentValidation;
using Lesson.Data;
using Lesson.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lesson.ResultPattern;

/// <summary>
/// Lesson 22-B — Railway-oriented pipeline chaining ErrorOr results.
///
/// "Railway-oriented programming" (ROP) connects steps in a pipeline:
/// if any step returns an error, the rest of the track is skipped.
/// ErrorOr supports this via Then / ThenAsync / Match.
///
/// Java parallel:
///   Vavr Either.flatMap / map → Then / ThenAsync
///   Optional.flatMap          → first check / guard
///   Stream pipeline           → chained Then steps
/// </summary>
public class AccountRopService(BankingDbContext db, IValidator<DepositRequest> validator)
{
    /// <summary>
    /// Deposit pipeline:
    ///   1. Validate request (FluentValidation → ErrorOr.Validation)
    ///   2. Load account (ErrorOr.NotFound if missing)
    ///   3. Apply business rule (ErrorOr.Validation if insufficient funds)
    ///   4. Persist
    ///
    /// Each step is a guard / Then; on first Error the chain short-circuits.
    /// </summary>
    public async Task<ErrorOr<AccountResultDto>> DepositAsync(
        int id, decimal amount, CancellationToken ct = default)
    {
        // Step 1: validate the request with FluentValidation → ErrorOr
        var req = new DepositRequest(id, amount);
        var validation = await validator.ValidateAsync(req, ct);
        if (!validation.IsValid)
            return validation.Errors
                .Select(e => Error.Validation(e.PropertyName, e.ErrorMessage))
                .ToList();

        // Step 2: load account
        var entity = await db.BankAccounts.FindAsync([id], ct);
        if (entity is null)
            return AccountResultService.Errors.NotFound(id);

        // Step 3: business rule (demonstrate a domain check inside the pipeline)
        if (amount <= 0)
            return Error.Validation("Deposit.Amount", "Deposit amount must be positive");

        // Step 4: persist
        entity.Balance += amount;
        await db.SaveChangesAsync(ct);

        return new AccountResultDto(entity.Id, entity.AccountNumber, entity.OwnerName, entity.Balance);
    }

    /// <summary>Transfer pipeline — demonstrates chaining multiple async steps.</summary>
    public async Task<ErrorOr<TransferResultDto>> TransferAsync(
        int fromId, int toId, decimal amount, CancellationToken ct = default)
    {
        if (amount <= 0)
            return Error.Validation("Transfer.Amount", "Transfer amount must be positive");

        var from = await db.BankAccounts.FindAsync([fromId], ct);
        if (from is null) return AccountResultService.Errors.NotFound(fromId);

        var to = await db.BankAccounts.FindAsync([toId], ct);
        if (to is null) return AccountResultService.Errors.NotFound(toId);

        if (from.Balance < amount)
            return Error.Validation("Transfer.Funds", "Insufficient funds");

        from.Balance -= amount;
        to.Balance   += amount;
        await db.SaveChangesAsync(ct);

        return new TransferResultDto(fromId, toId, amount, from.Balance, to.Balance);
    }
}

public record DepositRequest(int AccountId, decimal Amount);
public record TransferResultDto(int FromId, int ToId, decimal Amount, decimal FromBalance, decimal ToBalance);

/// <summary>
/// Lesson 22-B — FluentValidation validator that plugs into the ErrorOr pipeline.
/// </summary>
public class DepositRequestValidator : AbstractValidator<DepositRequest>
{
    public DepositRequestValidator()
    {
        RuleFor(x => x.AccountId).GreaterThan(0);
        RuleFor(x => x.Amount).GreaterThan(0).WithMessage("Deposit amount must be positive");
    }
}
