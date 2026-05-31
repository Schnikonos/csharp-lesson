using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lesson.Data;
using Lesson.EventSourcing;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lesson.Tests;

// ═══════════════════════════════════════════════════════════════════════════
// Unit tests — pure domain logic, no HTTP, no DI
// ═══════════════════════════════════════════════════════════════════════════

public class BankAccountAggregateTests
{
    [Fact]
    public void Open_CreatesAccountWithCorrectBalance()
    {
        var account = BankAccountAggregate.Open("ACC001", "Alice", 500m);
        account.Balance.Should().Be(500m);
        account.Owner.Should().Be("Alice");
        account.IsClosed.Should().BeFalse();
        account.UncommittedEvents.Should().HaveCount(1);
        account.UncommittedEvents[0].Should().BeOfType<AccountOpened>();
    }

    [Fact]
    public void Deposit_IncreasesBalance_AndRaisesEvent()
    {
        var account = BankAccountAggregate.Open("ACC002", "Bob", 100m);
        account.MarkCommitted();

        account.Deposit(250m, "Salary");

        account.Balance.Should().Be(350m);
        account.UncommittedEvents.Should().HaveCount(1);
        account.UncommittedEvents[0].Should().BeOfType<MoneyDeposited>();
    }

    [Fact]
    public void Withdraw_DecreasesBalance_AndRaisesEvent()
    {
        var account = BankAccountAggregate.Open("ACC003", "Carol", 1000m);
        account.MarkCommitted();

        account.Withdraw(300m, "Rent");

        account.Balance.Should().Be(700m);
        account.UncommittedEvents[0].Should().BeOfType<MoneyWithdrawn>();
    }

    [Fact]
    public void Withdraw_InsufficientFunds_Throws()
    {
        var account = BankAccountAggregate.Open("ACC004", "Dave", 50m);
        account.MarkCommitted();

        var act = () => account.Withdraw(100m);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Insufficient*");
    }

    [Fact]
    public void Rehydrate_RebuildsStateFromEvents()
    {
        var original = BankAccountAggregate.Open("ACC005", "Eve", 200m);
        original.Deposit(300m, "Bonus");
        original.Withdraw(50m, "Coffee");
        var events = original.UncommittedEvents.ToList();

        var rehydrated = BankAccountAggregate.Rehydrate(events);

        rehydrated.Balance.Should().Be(450m);
        rehydrated.Version.Should().Be(3);
        rehydrated.UncommittedEvents.Should().BeEmpty();
    }

    [Fact]
    public void Close_MarksAccountClosed()
    {
        var account = BankAccountAggregate.Open("ACC006", "Frank", 0m);
        account.MarkCommitted();

        account.Close();

        account.IsClosed.Should().BeTrue();
    }

    [Fact]
    public void Close_AlreadyClosed_Throws()
    {
        var account = BankAccountAggregate.Open("ACC007", "Grace", 0m);
        account.Close();
        account.MarkCommitted();

        var act = () => account.Close();

        act.Should().Throw<InvalidOperationException>().WithMessage("*already closed*");
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// Integration tests — HTTP endpoints via WebApplicationFactory
// ═══════════════════════════════════════════════════════════════════════════

public class EventSourcingFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    public EventSourcingFactory() => _connection.Open();
    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder) =>
        builder.ConfigureServices(services =>
        {
            var d = services.SingleOrDefault(s => s.ServiceType == typeof(DbContextOptions<BankingDbContext>));
            if (d is not null) services.Remove(d);
            services.AddDbContext<BankingDbContext>(o => o.UseSqlite(_connection));
            var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            scope.ServiceProvider.GetRequiredService<BankingDbContext>().Database.Migrate();
        });
    protected override void Dispose(bool d) { base.Dispose(d); if (d) _connection.Dispose(); }
}

public class EventSourcingIntegrationTests(EventSourcingFactory factory)
    : IClassFixture<EventSourcingFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<Guid> OpenAccountAsync(string number, string owner, decimal balance)
    {
        var response = await _client.PostAsJsonAsync("/event-sourcing/accounts",
            new { accountNumber = number, owner, initialBalance = balance });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Open_ReturnsCreated_WithId()
    {
        var response = await _client.PostAsJsonAsync("/event-sourcing/accounts",
            new { accountNumber = "TEST-001", owner = "Alice", initialBalance = 500m });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("id").GetGuid().Should().NotBeEmpty();
        body.GetProperty("balance").GetDecimal().Should().Be(500m);
    }

    [Fact]
    public async Task Deposit_IncreasesBalance()
    {
        var id = await OpenAccountAsync("TEST-002", "Bob", 100m);

        var response = await _client.PostAsJsonAsync(
            $"/event-sourcing/accounts/{id}/deposit",
            new { amount = 400m, description = "Salary" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("balance").GetDecimal().Should().Be(500m);
    }

    [Fact]
    public async Task Withdraw_DecreasesBalance()
    {
        var id = await OpenAccountAsync("TEST-003", "Carol", 1000m);

        var response = await _client.PostAsJsonAsync(
            $"/event-sourcing/accounts/{id}/withdraw",
            new { amount = 250m, description = "Rent" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("balance").GetDecimal().Should().Be(750m);
    }

    [Fact]
    public async Task Withdraw_InsufficientFunds_ReturnsBadRequest()
    {
        var id = await OpenAccountAsync("TEST-004", "Dave", 50m);

        var response = await _client.PostAsJsonAsync(
            $"/event-sourcing/accounts/{id}/withdraw",
            new { amount = 200m, description = "Big purchase" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetState_ReturnsCurrentState()
    {
        var id = await OpenAccountAsync("TEST-005", "Eve", 300m);
        await _client.PostAsJsonAsync($"/event-sourcing/accounts/{id}/deposit", new { amount = 200m });

        var response = await _client.GetAsync($"/event-sourcing/accounts/{id}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("balance").GetDecimal().Should().Be(500m);
        body.GetProperty("version").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task GetHistory_ReturnsAllEvents()
    {
        var id = await OpenAccountAsync("TEST-006", "Frank", 100m);
        await _client.PostAsJsonAsync($"/event-sourcing/accounts/{id}/deposit", new { amount = 50m });
        await _client.PostAsJsonAsync($"/event-sourcing/accounts/{id}/withdraw", new { amount = 30m });

        var response = await _client.GetAsync($"/event-sourcing/accounts/{id}/history");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var events = await response.Content.ReadFromJsonAsync<JsonElement[]>();
        events.Should().HaveCount(3);
        events![0].GetProperty("type").GetString().Should().Be("AccountOpened");
        events![1].GetProperty("type").GetString().Should().Be("MoneyDeposited");
        events![2].GetProperty("type").GetString().Should().Be("MoneyWithdrawn");
    }
}
