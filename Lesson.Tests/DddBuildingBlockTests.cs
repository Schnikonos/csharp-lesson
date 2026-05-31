using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Lesson.Domain;
using Lesson.Ddd;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Lesson.Data;

namespace Lesson.Tests;

/// <summary>
/// Lesson 19-A — DDD building blocks: AggregateRoot, ValueObject, Money, BankAccountAggregate.
///
/// We test the domain model in pure unit tests (no HTTP) and
/// verify that domain rules are enforced before any infrastructure is involved.
/// </summary>
public class DddBuildingBlockTests
{
    // ── Money value object ─────────────────────────────────────────────────

    [Fact]
    public void Money_StructuralEquality_TwoSameValuesAreEqual()
    {
        var a = new Money(100m, "USD");
        var b = new Money(100m, "USD");
        a.Should().Be(b);
    }

    [Fact]
    public void Money_Add_SameCurrency_Succeeds()
    {
        var result = new Money(100m, "USD").Add(new Money(50m, "USD"));
        result.Should().Be(new Money(150m, "USD"));
    }

    [Fact]
    public void Money_Add_DifferentCurrencies_ThrowsDomainException()
    {
        var act = () => new Money(100m, "USD").Add(new Money(50m, "EUR"));
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Money_Subtract_InsufficientFunds_ThrowsDomainException()
    {
        var act = () => new Money(50m, "USD").Subtract(new Money(100m, "USD"));
        act.Should().Throw<DomainException>().WithMessage("Insufficient funds");
    }

    // ── BankAccountAggregate ───────────────────────────────────────────────

    [Fact]
    public void Open_ValidData_CreatesAggregateWithDomainEvent()
    {
        var account = BankAccountAggregate.Open("AGG-001", "Alice", new Money(1000m, "USD"));

        account.AccountNumber.Should().Be("AGG-001");
        account.Balance.Should().Be(new Money(1000m, "USD"));
        account.PopDomainEvents().Should().HaveCount(1);
    }

    [Fact]
    public void Open_EmptyAccountNumber_ThrowsDomainException()
    {
        var act = () => BankAccountAggregate.Open("", "Alice", new Money(100m, "USD"));
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Deposit_PositiveAmount_UpdatesBalance()
    {
        var account = BankAccountAggregate.Open("AGG-002", "Bob", new Money(500m, "USD"));
        account.Deposit(new Money(200m, "USD"));
        account.Balance.Amount.Should().Be(700m);
    }

    [Fact]
    public void Withdraw_ExceedsBalance_ThrowsDomainException()
    {
        var account = BankAccountAggregate.Open("AGG-003", "Carol", new Money(100m, "USD"));
        var act = () => account.Withdraw(new Money(200m, "USD"));
        act.Should().Throw<DomainException>().WithMessage("Insufficient funds");
    }

    [Fact]
    public void PopDomainEvents_ClearsEventList()
    {
        var account = BankAccountAggregate.Open("AGG-004", "Dave", new Money(100m, "USD"));
        var first  = account.PopDomainEvents();
        var second = account.PopDomainEvents();

        first.Should().HaveCount(1);
        second.Should().BeEmpty();
    }
}
