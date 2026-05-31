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
/// Lesson 22-B — Railway-oriented pipeline tests.
///
/// Proves short-circuit behaviour: a failed step stops the chain and
/// produces the correct HTTP error code.
/// </summary>
public class ResultPipelineTests : IClassFixture<ResultPipelineFactory>
{
    private readonly ResultPipelineFactory _factory;
    public ResultPipelineTests(ResultPipelineFactory factory) => _factory = factory;
    private HttpClient Client() => _factory.CreateClient();

    private async Task<int> CreateAccountAsync(string number, decimal balance = 500m)
    {
        var r = await Client().PostAsJsonAsync("/result/accounts",
            new { accountNumber = number, ownerName = "Test", initialBalance = balance });
        var body = await r.Content.ReadFromJsonAsync<IdDto2>();
        return body!.Id;
    }

    // Valid deposit returns 200 with updated balance
    [Fact]
    public async Task Deposit_Valid_Returns200()
    {
        var id = await CreateAccountAsync("PIP-001");
        var r  = await Client().PostAsJsonAsync($"/result/pipeline/{id}/deposit", new { amount = 100m });
        r.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await r.Content.ReadFromJsonAsync<BalanceDto>();
        body!.Balance.Should().Be(600m);
    }

    // Deposit zero amount is caught by FluentValidation → 400
    [Fact]
    public async Task Deposit_ZeroAmount_Returns400()
    {
        var id = await CreateAccountAsync("PIP-002");
        var r  = await Client().PostAsJsonAsync($"/result/pipeline/{id}/deposit", new { amount = 0m });
        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // Deposit to unknown account → 404
    [Fact]
    public async Task Deposit_UnknownAccount_Returns404()
    {
        var r = await Client().PostAsJsonAsync("/result/pipeline/99999/deposit", new { amount = 50m });
        r.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // Transfer with insufficient funds returns 400 Validation error
    [Fact]
    public async Task Transfer_InsufficientFunds_Returns400()
    {
        var from = await CreateAccountAsync("PIP-003", 100m);
        var to   = await CreateAccountAsync("PIP-004", 100m);
        var r    = await Client().PostAsJsonAsync("/result/pipeline/transfer",
            new { fromId = from, toId = to, amount = 9999m });
        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}

file record IdDto2(int Id);
file record BalanceDto(int Id, string AccountNumber, string OwnerName, decimal Balance);

public class ResultPipelineFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection;
    public ResultPipelineFactory() { _connection = new("DataSource=:memory:"); _connection.Open(); }
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
