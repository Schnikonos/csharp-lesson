using Lesson.Domain;
using Lesson.Entities;

namespace Lesson.Tests;

/// <summary>
/// Lesson 12-A — xUnit basics: [Fact], [Theory], [InlineData], Arrange/Act/Assert.
///
/// These are PURE unit tests — no HTTP, no database, no DI container.
/// Each test creates its dependencies directly, making them fast and isolated.
///
/// Java parallel:
///   @Test                         → [Fact]
///   @ParameterizedTest + @ValueSource → [Theory] + [InlineData]
///   assertEquals(expected, actual) → Assert.Equal(expected, actual)
///   assertThrows(Type, () → ...)  → Assert.Throws<TException>(() => ...)
/// </summary>
public class BankAccountDomainTests
{
    // Shared instance — BankAccountDomainService has no state, safe to reuse.
    private readonly BankAccountDomainService _svc = new();

    // ── Helper ────────────────────────────────────────────────────────────────
    private static BankAccount ActiveAccount(decimal balance = 0) =>
        new() { AccountNumber = "BANK-001", OwnerName = "Alice", Balance = balance, IsActive = true };

    // ── Deposit ───────────────────────────────────────────────────────────────

    [Fact]
    public void Deposit_PositiveAmount_IncreasesBalance()
    {
        // Arrange
        var account = ActiveAccount(balance: 100m);

        // Act
        _svc.Deposit(account, 50m);

        // Assert
        Assert.Equal(150m, account.Balance);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void Deposit_NonPositiveAmount_Throws(decimal amount)
    {
        var account = ActiveAccount(100m);
        Assert.Throws<ArgumentOutOfRangeException>(() => _svc.Deposit(account, amount));
    }

    // ── Withdraw ──────────────────────────────────────────────────────────────

    [Fact]
    public void Withdraw_SufficientFunds_DecreasesBalance()
    {
        var account = ActiveAccount(balance: 200m);
        _svc.Withdraw(account, 80m);
        Assert.Equal(120m, account.Balance);
    }

    [Fact]
    public void Withdraw_ExactBalance_LeavesZero()
    {
        var account = ActiveAccount(balance: 50m);
        _svc.Withdraw(account, 50m);
        Assert.Equal(0m, account.Balance);
    }

    [Fact]
    public void Withdraw_InsufficientFunds_ThrowsInvalidOperation()
    {
        var account = ActiveAccount(balance: 10m);
        Assert.Throws<InvalidOperationException>(() => _svc.Withdraw(account, 20m));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Withdraw_NonPositiveAmount_ThrowsArgumentOutOfRange(decimal amount)
    {
        var account = ActiveAccount(100m);
        Assert.Throws<ArgumentOutOfRangeException>(() => _svc.Withdraw(account, amount));
    }

    // ── CanClose ──────────────────────────────────────────────────────────────

    [Fact]
    public void CanClose_ActiveWithZeroBalance_ReturnsTrue()
    {
        var account = ActiveAccount(balance: 0m);
        Assert.True(_svc.CanClose(account));
    }

    [Fact]
    public void CanClose_NonZeroBalance_ReturnsFalse()
    {
        var account = ActiveAccount(balance: 10m);
        Assert.False(_svc.CanClose(account));
    }

    [Fact]
    public void CanClose_InactiveAccount_ReturnsFalse()
    {
        var account = ActiveAccount(balance: 0m);
        account.IsActive = false;
        Assert.False(_svc.CanClose(account));
    }

    // ── Account number generation ─────────────────────────────────────────────

    [Theory]
    [InlineData("2024-01-15", 1,    "BANK-20240115-0001")]
    [InlineData("2024-12-31", 9999, "BANK-20241231-9999")]
    [InlineData("2000-06-01", 42,   "BANK-20000601-0042")]
    public void GenerateAccountNumber_FormatsCorrectly(string dateStr, int seq, string expected)
    {
        var date   = DateOnly.Parse(dateStr);
        var result = _svc.GenerateAccountNumber(date, seq);
        Assert.Equal(expected, result);
    }

    // ── Interest calculation ──────────────────────────────────────────────────

    [Theory]
    [InlineData(10_000, 3.65, 1.00)]   // 10 000 × 3.65% / 365 = 1.00 exactly
    [InlineData(0,      5.00, 0.00)]   // zero balance → zero interest
    [InlineData(1_000,  5.00, 0.14)]   // 1 000 × 5% / 365 ≈ 0.1370 → rounded to 0.14
    public void CalculateDailyInterest_ReturnsCorrectValue(
        decimal balance, decimal rate, decimal expected)
    {
        var result = _svc.CalculateDailyInterest(balance, rate);
        Assert.Equal(expected, result);
    }
}
