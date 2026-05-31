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
/// Lesson 19-C — AggregateUnitOfWork, post-commit domain events, optimistic concurrency.
///
/// Tests exercise the /ddd/advanced endpoints which use AggregateUnitOfWork
/// instead of calling db.SaveChangesAsync directly.
/// </summary>
public class DddAdvancedTests : IClassFixture<DddAdvancedFactory>
{
    private readonly DddAdvancedFactory _factory;
    public DddAdvancedTests(DddAdvancedFactory factory) => _factory = factory;
    private HttpClient Client() => _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private async Task<(int id, string rowVersion)> OpenAndGetAsync(string suffix)
    {
        var create = await Client().PostAsJsonAsync("/ddd/advanced/accounts",
            new { accountNumber = $"ADV-{suffix}", ownerName = "Alice", initialBalance = 1000m });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await create.Content.ReadFromJsonAsync<AdvCreatedDto>();

        var get = await Client().GetFromJsonAsync<AdvAccountDto>($"/ddd/advanced/accounts/{body!.Id}");
        return (body.Id, get!.RowVersion);
    }

    // POST creates account and returns 201
    [Fact]
    public async Task Open_Returns201()
    {
        var r = await Client().PostAsJsonAsync("/ddd/advanced/accounts",
            new { accountNumber = "ADV-C01", ownerName = "Bob", initialBalance = 500m });
        r.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    // GET returns account with RowVersion header
    [Fact]
    public async Task GetById_ReturnsRowVersion()
    {
        var (id, rowVersion) = await OpenAndGetAsync($"C{Random.Shared.Next(100,999)}");
        rowVersion.Should().NotBeNullOrEmpty();
    }

    // Deposit with correct RowVersion succeeds
    [Fact]
    public async Task Deposit_WithCorrectRowVersion_Returns200()
    {
        var (id, rowVersion) = await OpenAndGetAsync($"C{Random.Shared.Next(100,999)}");
        var r = await Client().PostAsJsonAsync($"/ddd/advanced/accounts/{id}/deposit",
            new { amount = 100m, rowVersion });
        r.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // Deposit with stale RowVersion returns 409 Conflict
    [Fact]
    public async Task Deposit_WithStaleRowVersion_Returns409()
    {
        var (id, rowVersion) = await OpenAndGetAsync($"C{Random.Shared.Next(100,999)}");

        // First deposit changes the RowVersion
        await Client().PostAsJsonAsync($"/ddd/advanced/accounts/{id}/deposit",
            new { amount = 50m, rowVersion });

        // Second deposit using old RowVersion should conflict
        var r = await Client().PostAsJsonAsync($"/ddd/advanced/accounts/{id}/deposit",
            new { amount = 50m, rowVersion });  // stale
        r.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
}

file record AdvCreatedDto(int Id);
file record AdvAccountDto(int Id, string AccountNumber, string OwnerName, decimal Balance, string RowVersion);

public class DddAdvancedFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection;
    public DddAdvancedFactory() { _connection = new("DataSource=:memory:"); _connection.Open(); }
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
