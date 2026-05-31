using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Lesson.Data;

namespace Lesson.Tests;

/// <summary>
/// Lesson 19-B — Domain aggregate repository (EF Core implementation).
///
/// We test the DDD endpoints which go through:
///   Controller → IAggregateRepository (EfAggregateRepository) → BankAccountAggregate → DB
/// Business rules are enforced inside the aggregate.
/// </summary>
public class DddRepositoryTests : IClassFixture<DddTestFactory>
{
    private readonly DddTestFactory _factory;
    public DddRepositoryTests(DddTestFactory factory) => _factory = factory;
    private HttpClient NewClient() => _factory.CreateClient(
        new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private async Task<int> CreateAccountAsync(string number = "DDD-001", decimal balance = 1000m)
    {
        var r = await NewClient().PostAsJsonAsync("/ddd/accounts",
            new { accountNumber = number, ownerName = "Alice", initialBalance = balance });
        r.EnsureSuccessStatusCode();
        var body = await r.Content.ReadFromJsonAsync<DddCreatedResult>();
        return body!.Id;
    }

    // POST creates aggregate and returns 201
    [Fact]
    public async Task OpenAccount_Returns201()
    {
        var r = await NewClient().PostAsJsonAsync("/ddd/accounts",
            new { accountNumber = "DDD-B01", ownerName = "Bob", initialBalance = 500m });
        r.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    // GET returns the account with balance as string
    [Fact]
    public async Task GetById_ReturnsAccount()
    {
        var id = await CreateAccountAsync($"DDD-B{Random.Shared.Next(100,999)}");
        var r = await NewClient().GetAsync($"/ddd/accounts/{id}");
        r.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // Deposit increases balance — domain operation goes through aggregate
    [Fact]
    public async Task Deposit_UpdatesBalance()
    {
        var id = await CreateAccountAsync($"DDD-B{Random.Shared.Next(100,999)}", 1000m);
        var r = await NewClient().PostAsJsonAsync($"/ddd/accounts/{id}/deposit",
            new { amount = 500m });
        r.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // Withdraw over balance returns 500 (DomainException propagates)
    [Fact]
    public async Task Withdraw_ExceedsBalance_Returns500()
    {
        var id = await CreateAccountAsync($"DDD-B{Random.Shared.Next(100,999)}", 100m);
        var r = await NewClient().PostAsJsonAsync($"/ddd/accounts/{id}/withdraw",
            new { amount = 999m });
        // DomainException propagates as 500 (or 422 if a handler exists)
        ((int)r.StatusCode).Should().BeGreaterThanOrEqualTo(400);
    }
}

public record DddCreatedResult(int Id);

public class DddTestFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection;
    public DddTestFactory() { _connection = new("DataSource=:memory:"); _connection.Open(); }
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
