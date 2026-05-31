using ErrorOr;
using Lesson.Data;
using Microsoft.EntityFrameworkCore;

namespace Lesson.ResultPattern;

/// <summary>
/// Lesson 22-A — Result pattern: replace thrown exceptions with ErrorOr returns.
///
/// The service layer returns ErrorOr&lt;T&gt; instead of throwing domain exceptions.
/// The controller pattern-matches on the result to produce the correct HTTP response.
///
/// ErrorOr&lt;T&gt; is either:
///   Success → contains the value T
///   Failure → contains one or more Error descriptors
///
/// Java parallel:
///   Vavr Either&lt;AppError, T&gt; / Try&lt;T&gt;
///   Result types popularized by functional Java frameworks (Arrow-kt in Kotlin)
/// </summary>
public class AccountResultService(BankingDbContext db)
{
    // Domain error catalogue — keeps error semantics in one place
    public static class Errors
    {
        public static Error NotFound(int id)    => Error.NotFound("Account.NotFound",    $"Account {id} not found");
        public static Error Duplicate(string n) => Error.Conflict("Account.Duplicate",  $"Account number '{n}' already exists");
        public static Error NegativeBalance()   => Error.Validation("Account.Balance",  "Initial balance must be non-negative");
    }

    /// <summary>Get account by id — returns NotFound error instead of throwing.</summary>
    public async Task<ErrorOr<AccountResultDto>> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var a = await db.BankAccounts.FindAsync([id], ct);
        if (a is null) return Errors.NotFound(id);
        return new AccountResultDto(a.Id, a.AccountNumber, a.OwnerName, a.Balance);
    }

    /// <summary>Create account — validates and returns domain errors as ErrorOr failures.</summary>
    public async Task<ErrorOr<AccountResultDto>> CreateAsync(
        string accountNumber, string ownerName, decimal initialBalance,
        CancellationToken ct = default)
    {
        if (initialBalance < 0)
            return Errors.NegativeBalance();

        var exists = await db.BankAccounts
            .AnyAsync(a => a.AccountNumber == accountNumber, ct);
        if (exists)
            return Errors.Duplicate(accountNumber);

        var account = new Lesson.Entities.BankAccount
        {
            AccountNumber = accountNumber,
            OwnerName     = ownerName,
            Balance       = initialBalance,
            AccountType   = "Savings",
        };
        db.BankAccounts.Add(account);
        await db.SaveChangesAsync(ct);
        return new AccountResultDto(account.Id, account.AccountNumber, account.OwnerName, account.Balance);
    }
}

public record AccountResultDto(int Id, string AccountNumber, string OwnerName, decimal Balance);
