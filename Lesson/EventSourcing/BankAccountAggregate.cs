namespace Lesson.EventSourcing;

/// <summary>
/// Bank account rebuilt purely from its event stream — no ORM, no database row.
/// This is the core pattern of event sourcing: state = fold(events).
/// </summary>
public class BankAccountAggregate
{
    public Guid Id { get; private set; }
    public string AccountNumber { get; private set; } = string.Empty;
    public string Owner { get; private set; } = string.Empty;
    public decimal Balance { get; private set; }
    public bool IsClosed { get; private set; }
    public int Version { get; private set; }

    private readonly List<IEvent> _uncommittedEvents = [];
    public IReadOnlyList<IEvent> UncommittedEvents => _uncommittedEvents;

    // Private constructor — use static factory or Rehydrate
    private BankAccountAggregate() { }

    // ── Commands ──────────────────────────────────────────────────────────

    public static BankAccountAggregate Open(string accountNumber, string owner, decimal initialBalance)
    {
        if (initialBalance < 0) throw new InvalidOperationException("Initial balance cannot be negative.");
        var account = new BankAccountAggregate();
        account.Apply(new AccountOpened(Guid.NewGuid(), accountNumber, owner, initialBalance, DateTimeOffset.UtcNow));
        return account;
    }

    public void Deposit(decimal amount, string description = "")
    {
        if (IsClosed) throw new InvalidOperationException("Cannot deposit into a closed account.");
        if (amount <= 0) throw new InvalidOperationException("Deposit amount must be positive.");
        Apply(new MoneyDeposited(Id, amount, description, DateTimeOffset.UtcNow));
    }

    public void Withdraw(decimal amount, string description = "")
    {
        if (IsClosed) throw new InvalidOperationException("Cannot withdraw from a closed account.");
        if (amount <= 0) throw new InvalidOperationException("Withdrawal amount must be positive.");
        if (amount > Balance) throw new InvalidOperationException("Insufficient funds.");
        Apply(new MoneyWithdrawn(Id, amount, description, DateTimeOffset.UtcNow));
    }

    public void Close()
    {
        if (IsClosed) throw new InvalidOperationException("Account is already closed.");
        Apply(new AccountClosed(Id, DateTimeOffset.UtcNow));
    }

    // ── Rehydration ───────────────────────────────────────────────────────

    /// <summary>
    /// Rebuild the aggregate from a persisted event stream (no uncommitted events).
    /// </summary>
    public static BankAccountAggregate Rehydrate(IEnumerable<IEvent> events)
    {
        var account = new BankAccountAggregate();
        foreach (var e in events)
            account.Mutate(e);
        return account;
    }

    // ── Internal event application ────────────────────────────────────────

    private void Apply(IEvent @event)
    {
        Mutate(@event);
        _uncommittedEvents.Add(@event);
    }

    private void Mutate(IEvent @event)
    {
        switch (@event)
        {
            case AccountOpened e:
                Id = e.AggregateId;
                AccountNumber = e.AccountNumber;
                Owner = e.Owner;
                Balance = e.InitialBalance;
                break;
            case MoneyDeposited e:
                Balance += e.Amount;
                break;
            case MoneyWithdrawn e:
                Balance -= e.Amount;
                break;
            case AccountClosed:
                IsClosed = true;
                break;
        }
        Version++;
    }

    public void MarkCommitted() => _uncommittedEvents.Clear();
}
