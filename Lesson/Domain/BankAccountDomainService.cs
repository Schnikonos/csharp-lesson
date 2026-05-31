using Lesson.Entities;

namespace Lesson.Domain;

/// <summary>
/// Lesson 12-A — Pure domain logic extracted for unit testing.
///
/// This service contains business rules that operate on plain objects
/// (no database, no HTTP). Perfect for pure unit tests: fast, isolated,
/// deterministic.
///
/// Java parallel: a @Service with no @Repository injection — just business logic.
/// </summary>
public class BankAccountDomainService
{
    // ── Balance rules ─────────────────────────────────────────────────────────

    /// <summary>
    /// Returns true when the account can be closed:
    ///   - not already deleted/inactive
    ///   - balance is zero
    /// </summary>
    public bool CanClose(BankAccount account) =>
        account.IsActive && !account.IsDeleted && account.Balance == 0;

    /// <summary>
    /// Applies a deposit to the account.  Throws when amount ≤ 0.
    /// Java parallel: service method validated with @Positive + manual check.
    /// </summary>
    public void Deposit(BankAccount account, decimal amount)
    {
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Deposit amount must be positive.");
        account.Balance += amount;
    }

    /// <summary>
    /// Applies a withdrawal.  Throws when amount ≤ 0 or insufficient funds.
    /// </summary>
    public void Withdraw(BankAccount account, decimal amount)
    {
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Withdrawal amount must be positive.");
        if (amount > account.Balance)
            throw new InvalidOperationException("Insufficient funds.");
        account.Balance -= amount;
    }

    /// <summary>
    /// Generates a standardized account number in the format BANK-YYYYMMDD-NNNN.
    /// Java parallel: a utility method returning a formatted string.
    /// </summary>
    public string GenerateAccountNumber(DateOnly date, int sequence) =>
        $"BANK-{date:yyyyMMdd}-{sequence:D4}";

    /// <summary>
    /// Returns the daily interest accrual for a savings account (annualRate / 365).
    /// Rounded to 2 decimal places to avoid floating-point drift in assertions.
    /// </summary>
    public decimal CalculateDailyInterest(decimal balance, decimal annualRatePercent) =>
        Math.Round(balance * (annualRatePercent / 100m) / 365m, 2);
}
