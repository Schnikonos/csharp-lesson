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
/// Lesson 22-A — Result pattern tests.
///
/// Proves that domain errors surface as typed HTTP status codes
/// without any try/catch in the service or controller.
///
/// Java parallel:
///   @Test for Either.left() → 404 / 409 / 400
///   @Test for Either.right() → 201 / 200
/// </summary>
public class ResultPatternBasicTests : IClassFixture<ResultPatternFactory>
{
    private readonly ResultPatternFactory _factory;
    public ResultPatternBasicTests(ResultPatternFactory factory) => _factory = factory;
    private HttpClient Client() => _factory.CreateClient();

    // Valid POST returns 201
    [Fact]
    public async Task Create_ValidAccount_Returns201()
    {
        var r = await Client().PostAsJsonAsync("/result/accounts",
            new { accountNumber = "RES-001", ownerName = "Alice", initialBalance = 500m });
        r.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    // Negative balance returns 400 (Validation error)
    [Fact]
    public async Task Create_NegativeBalance_Returns400()
    {
        var r = await Client().PostAsJsonAsync("/result/accounts",
            new { accountNumber = "RES-NEG", ownerName = "X", initialBalance = -1m });
        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // Duplicate account number returns 409 (Conflict error)
    [Fact]
    public async Task Create_DuplicateNumber_Returns409()
    {
        await Client().PostAsJsonAsync("/result/accounts",
            new { accountNumber = "RES-DUP", ownerName = "X", initialBalance = 100m });
        var r = await Client().PostAsJsonAsync("/result/accounts",
            new { accountNumber = "RES-DUP", ownerName = "Y", initialBalance = 200m });
        r.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // Unknown id returns 404 (NotFound error)
    [Fact]
    public async Task GetById_Unknown_Returns404()
    {
        var r = await Client().GetAsync("/result/accounts/99999");
        r.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}

public class ResultPatternFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection;
    public ResultPatternFactory() { _connection = new("DataSource=:memory:"); _connection.Open(); }
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
