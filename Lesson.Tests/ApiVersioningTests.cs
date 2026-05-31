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
/// Lesson 21-B — API versioning tests.
///
/// v1 returns AccountSummaryV1 (no AccountType).
/// v2 returns AccountSummaryV2 (includes AccountType + IsDeleted).
///
/// Java parallel:
///   Spring @ApiVersion test with MockMvc GET /v1/... and /v2/...
/// </summary>
public class ApiVersioningTests : IClassFixture<ApiVersioningFactory>
{
    private readonly ApiVersioningFactory _factory;
    public ApiVersioningTests(ApiVersioningFactory factory) => _factory = factory;
    private HttpClient Client() => _factory.CreateClient();

    private async Task<int> CreateAccountAsync(string number)
    {
        var r = await Client().PostAsJsonAsync("/v1/versioned/accounts",
            new { accountNumber = number, ownerName = "Test", initialBalance = 100m });
        var body = await r.Content.ReadFromJsonAsync<IdDto>();
        return body!.Id;
    }

    // v1 endpoint returns 200 and a list of basic DTOs
    [Fact]
    public async Task V1_Get_Returns200()
    {
        await CreateAccountAsync("VER-001");
        var r = await Client().GetAsync("/v1/versioned/accounts");
        r.StatusCode.Should().Be(HttpStatusCode.OK);
        var list = await r.Content.ReadFromJsonAsync<List<V1Dto>>();
        list.Should().NotBeNull();
        list.Should().Contain(a => a.AccountNumber == "VER-001");
    }

    // v2 endpoint returns 200 and enriched DTOs with AccountType
    [Fact]
    public async Task V2_Get_Returns200WithAccountType()
    {
        await CreateAccountAsync("VER-002");
        var r = await Client().GetAsync("/v2/versioned/accounts");
        r.StatusCode.Should().Be(HttpStatusCode.OK);
        var list = await r.Content.ReadFromJsonAsync<List<V2Dto>>();
        list.Should().Contain(a => a.AccountNumber == "VER-002" && a.AccountType == "Savings");
    }

    // v1 and v2 responses differ: v1 DTO has no AccountType field
    [Fact]
    public async Task V1_ResponseDoesNotContainAccountType()
    {
        await CreateAccountAsync("VER-003");
        var json = await Client().GetStringAsync("/v1/versioned/accounts");
        json.Should().NotContain("\"accountType\"");
    }

    // Response headers include api-supported-versions
    [Fact]
    public async Task Response_ContainsSupportedVersionsHeader()
    {
        var r = await Client().GetAsync("/v1/versioned/accounts");
        r.Headers.Should().ContainKey("api-supported-versions");
    }
}

file record IdDto(int Id);
file record V1Dto(int Id, string AccountNumber, string OwnerName, decimal Balance);
file record V2Dto(int Id, string AccountNumber, string OwnerName, decimal Balance, string AccountType, bool IsDeleted);

public class ApiVersioningFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection;
    public ApiVersioningFactory() { _connection = new("DataSource=:memory:"); _connection.Open(); }
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
