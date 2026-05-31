using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Lesson.Data;
using Lesson.MinimalApi;

namespace Lesson.Tests;

/// <summary>
/// Lesson 21-A — Minimal API tests: CRUD endpoints + IEndpointFilter validation.
///
/// Java parallel:
///   @SpringBootTest MockMvc GET/POST on /minimal/accounts
///   @Valid + MethodArgumentNotValidException → 400 ValidationProblem
/// </summary>
public class MinimalApiBasicTests : IClassFixture<MinimalApiBasicFactory>
{
    private readonly MinimalApiBasicFactory _factory;
    public MinimalApiBasicTests(MinimalApiBasicFactory factory) => _factory = factory;
    private HttpClient Client() => _factory.CreateClient();

    // POST creates an account and returns 201 Created
    [Fact]
    public async Task Post_ValidAccount_Returns201()
    {
        var r = await Client().PostAsJsonAsync("/minimal/accounts",
            new { accountNumber = "MIN-001", ownerName = "Alice", initialBalance = 1000m });
        r.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await r.Content.ReadFromJsonAsync<AccountDto>();
        body!.Id.Should().BeGreaterThan(0);
        body.AccountNumber.Should().Be("MIN-001");
    }

    // POST with empty accountNumber triggers validation → 400
    [Fact]
    public async Task Post_InvalidAccount_Returns400()
    {
        var r = await Client().PostAsJsonAsync("/minimal/accounts",
            new { accountNumber = "", ownerName = "Bob", initialBalance = 500m });
        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // GET /minimal/accounts returns the created account
    [Fact]
    public async Task Get_ReturnsCreatedAccount()
    {
        var post = await Client().PostAsJsonAsync("/minimal/accounts",
            new { accountNumber = "MIN-002", ownerName = "Carol", initialBalance = 250m });
        var created = await post.Content.ReadFromJsonAsync<AccountDto>();

        var list = await Client().GetFromJsonAsync<List<AccountDto>>("/minimal/accounts");
        list.Should().Contain(a => a.Id == created!.Id && a.OwnerName == "Carol");
    }

    // GET /{id} returns 404 for unknown account
    [Fact]
    public async Task GetById_Unknown_Returns404()
    {
        var r = await Client().GetAsync("/minimal/accounts/99999");
        r.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}

public class MinimalApiBasicFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection;
    public MinimalApiBasicFactory() { _connection = new("DataSource=:memory:"); _connection.Open(); }
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
