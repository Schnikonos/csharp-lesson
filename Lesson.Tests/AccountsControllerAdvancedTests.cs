using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lesson.Controllers;
using Lesson.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lesson.Tests;

/// <summary>
/// Lesson 04-B — integration tests for pagination, projection, GroupBy aggregate,
/// and Any/All/Count endpoints on AccountsController.
/// Uses the shared in-memory SQLite connection pattern so migrations and requests
/// share the same database instance.
/// </summary>
public class AccountsControllerAdvancedTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly SqliteConnection _connection;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public AccountsControllerAdvancedTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
            host.ConfigureServices(services =>
            {
                var d = services.SingleOrDefault(
                    s => s.ServiceType == typeof(DbContextOptions<BankingDbContext>));
                if (d is not null) services.Remove(d);
                services.AddDbContext<BankingDbContext>(o => o.UseSqlite(_connection));
            }));

        _client = _factory.CreateClient();

        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<BankingDbContext>().Database.Migrate();
    }

    // ── Pagination + projection ───────────────────────────────────────────────

    [Fact]
    public async Task GetSummary_Page1_ReturnsPagedResult()
    {
        var response = await _client.GetAsync("/accounts/summary?page=1&pageSize=10");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = JsonSerializer.Deserialize<PagedResult<AccountSummaryDto>>(
            await response.Content.ReadAsStringAsync(), JsonOpts)!;

        result.Items.Should().NotBeEmpty();
        result.Page.Should().Be(1);
        result.PageSize.Should().Be(10);
        result.TotalCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GetSummary_PageSize1_ReturnsSingleItem()
    {
        var body = await _client.GetStringAsync("/accounts/summary?page=1&pageSize=1");
        var result = JsonSerializer.Deserialize<PagedResult<AccountSummaryDto>>(body, JsonOpts)!;

        result.Items.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetSummary_ProjectionDto_DoesNotExposeAuditFields()
    {
        var body = await _client.GetStringAsync("/accounts/summary?page=1&pageSize=10");

        // AccountSummaryDto only has Id, AccountNumber, OwnerName, AccountType, Balance, IsActive
        body.Should().NotContain("createdAt");
        body.Should().NotContain("updatedAt");
        body.Should().NotContain("rowVersion");
    }

    [Fact]
    public async Task GetSummary_InvalidPage_ReturnsBadRequest()
    {
        var response = await _client.GetAsync("/accounts/summary?page=0&pageSize=10");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── GroupBy aggregate ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetStats_ReturnsGroupedByAccountType()
    {
        var stats = await _client.GetFromJsonAsync<List<AccountTypeStatDto>>("/accounts/stats", JsonOpts);

        stats.Should().NotBeEmpty();
        stats!.Should().AllSatisfy(s =>
        {
            s.AccountType.Should().NotBeNullOrEmpty();
            s.Count.Should().BeGreaterThan(0);
            s.TotalBalance.Should().BeGreaterThan(0);
        });
    }

    [Fact]
    public async Task GetStats_SeededData_ContainsCheckingAndSavings()
    {
        var stats = await _client.GetFromJsonAsync<List<AccountTypeStatDto>>("/accounts/stats", JsonOpts);

        stats!.Select(s => s.AccountType).Should().Contain("Checking");
        stats!.Select(s => s.AccountType).Should().Contain("Savings");
    }

    // ── Any predicate ─────────────────────────────────────────────────────────

    [Fact]
    public async Task AnyHighBalance_BelowSeedValues_ReturnsTrue()
    {
        // Seeded accounts have balances 12 500 and 45 000
        var value = await _client.GetFromJsonAsync<bool>("/accounts/any-high-balance?threshold=10000");
        value.Should().BeTrue();
    }

    [Fact]
    public async Task AnyHighBalance_AboveSeedValues_ReturnsFalse()
    {
        var value = await _client.GetFromJsonAsync<bool>("/accounts/any-high-balance?threshold=1000000");
        value.Should().BeFalse();
    }

    // ── All predicate ─────────────────────────────────────────────────────────

    [Fact]
    public async Task AllPositive_SeededAccounts_ReturnsTrue()
    {
        // Both seeded accounts have positive balances
        var value = await _client.GetFromJsonAsync<bool>("/accounts/all-positive");
        value.Should().BeTrue();
    }

    // ── Count ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CountActive_NoFilter_ReturnsAllActiveAccounts()
    {
        var count = await _client.GetFromJsonAsync<int>("/accounts/count");
        count.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task CountActive_FilteredByType_SumsToTotal()
    {
        var savings  = await _client.GetFromJsonAsync<int>("/accounts/count?type=Savings");
        var checking = await _client.GetFromJsonAsync<int>("/accounts/count?type=Checking");
        var total    = await _client.GetFromJsonAsync<int>("/accounts/count");

        (savings + checking).Should().Be(total);
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
        _connection.Dispose();
    }
}
