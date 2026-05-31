using Lesson.Ddd;
using Lesson.Domain;
using Lesson.Features.Accounts.DomainEvents;

namespace Lesson.Domain;

/// <summary>
/// Lesson 19-A — BankAccountAggregate: aggregate root that owns invariants.
///
/// Unlike the data-anemic BankAccount EF entity, this aggregate:
///   - Enforces business rules before mutating state
///   - Raises domain events when something meaningful happens
///   - Uses Money value object instead of raw decimal
///
/// Note: for simplicity we keep the EF Core entity separate (anti-corruption layer).
/// In a full DDD project you would either use EF Core with owned entities or
/// the aggregate itself would map directly.
///
/// Java parallel:
///   Axon @Aggregate class with @CommandHandler methods
///   JPA @Entity inheriting from AbstractAggregateRoot
/// </summary>
public class BankAccountAggregate : AggregateRoot
{
    public int    Id            { get; internal set; }
    public string AccountNumber { get; private set; } = null!;
    public string OwnerName     { get; private set; } = null!;
    public Money  Balance       { get; private set; }

    private BankAccountAggregate() { }   // for ORM / factory

    /// <summary>Reconstruct from persistence (no invariants enforced — data already valid).</summary>
    internal static BankAccountAggregate Reconstruct(int id, string accountNumber, string ownerName, Money balance)
    {
        return new BankAccountAggregate
        {
            Id            = id,
            AccountNumber = accountNumber,
            OwnerName     = ownerName,
            Balance       = balance,
        };
    }

    /// <summary>Factory method — the only way to create a valid aggregate.</summary>
    public static BankAccountAggregate Open(
        string  accountNumber,
        string  ownerName,
        Money   initialBalance)
    {
        if (string.IsNullOrWhiteSpace(accountNumber))
            throw new DomainException("Account number is required");
        if (string.IsNullOrWhiteSpace(ownerName))
            throw new DomainException("Owner name is required");
        if (initialBalance.Amount < 0)
            throw new DomainException("Initial balance cannot be negative");

        var account = new BankAccountAggregate
        {
            AccountNumber = accountNumber,
            OwnerName     = ownerName,
            Balance       = initialBalance,
        };

        // Raise domain event — will be dispatched post-commit by the infrastructure
        account.RaiseDomainEvent(new AccountCreatedDomainEvent(
            account.Id, accountNumber, ownerName, initialBalance.Amount));

        return account;
    }

    /// <summary>Credits the account. Raises BalanceChanged domain event.</summary>
    public void Deposit(Money amount)
    {
        if (amount.Amount <= 0)
            throw new DomainException("Deposit amount must be positive");
        Balance = Balance.Add(amount);
    }

    /// <summary>Debits the account. Raises BalanceChanged domain event. Enforces overdraft rule.</summary>
    public void Withdraw(Money amount)
    {
        if (amount.Amount <= 0)
            throw new DomainException("Withdrawal amount must be positive");
        Balance = Balance.Subtract(amount);   // Money.Subtract throws if insufficient
    }
}
