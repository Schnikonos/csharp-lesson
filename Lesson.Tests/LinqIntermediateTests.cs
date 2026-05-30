using System.Net.Http.Json;
using System.Text.Json;
using Lesson.Controllers;
using Lesson.Data;
using Lesson.Models;
using Lesson.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lesson.Tests;

/// <summary>
/// Lesson 05-B integration tests — verifies:
///   • IEnumerable vs IQueryable (filter-in-memory / filter-lazy same results)
///   • GroupBy → CategorySummary
///   • Join → OrderLine
///   • SelectMany → flattened product labels
///   • let clause → DiscountedProduct
///   • Chaining → top N order lines
/// </summary>
public class LinqIntermediateTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly SqliteConnection _connection;
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public LinqIntermediateTests(WebApplicationFactory<Program> factory)
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _factory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(DbContextOptions<BankingDbContext>));
                if (descriptor is not null) services.Remove(descriptor);

                services.AddDbContext<BankingDbContext>(options =>
                    options.UseSqlite(_connection));

                var sp = services.BuildServiceProvider();
                using var scope = sp.CreateScope();
                scope.ServiceProvider
                     .GetRequiredService<BankingDbContext>()
                     .Database.Migrate();
            }));

        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
        _connection.Dispose();
    }

    // ── IEnumerable vs IQueryable ─────────────────────────────────────────────

    [Fact]
    public async Task FilterInMemory_SameResultAsFilterLazy_ForSameCategory()
    {
        var inMem = await _client.GetFromJsonAsync<List<ProductResponse>>(
            "/linq/filter-in-memory?category=Electronics", _json);
        var lazy = await _client.GetFromJsonAsync<List<ProductResponse>>(
            "/linq/filter-lazy?category=Electronics", _json);

        Assert.NotNull(inMem);
        Assert.NotNull(lazy);
        Assert.Equal(inMem.Select(p => p.Id).Order(), lazy.Select(p => p.Id).Order());
    }

    [Fact]
    public async Task FilterLazy_ReturnsOnlyMatchingCategory()
    {
        var products = await _client.GetFromJsonAsync<List<ProductResponse>>(
            "/linq/filter-lazy?category=Furniture", _json);
        Assert.NotNull(products);
        Assert.NotEmpty(products);
        Assert.All(products, p => Assert.Equal("Furniture", p.Category));
    }

    // ── GroupBy ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetCategorySummaries_ReturnsAllCategories()
    {
        var summaries = await _client.GetFromJsonAsync<List<CategorySummary>>(
            "/linq/categories/summary", _json);
        Assert.NotNull(summaries);
        Assert.NotEmpty(summaries);
        var expectedCategories = LinqIntermediateService.Products
            .Select(p => p.Category).Distinct().OrderBy(c => c).ToList();
        Assert.Equal(expectedCategories, summaries.Select(s => s.Category).ToList());
    }

    [Fact]
    public async Task GetCategorySummaries_CountsAreCorrect()
    {
        var summaries = await _client.GetFromJsonAsync<List<CategorySummary>>(
            "/linq/categories/summary", _json);
        Assert.NotNull(summaries);
        var expected = LinqIntermediateService.Products
            .GroupBy(p => p.Category)
            .ToDictionary(g => g.Key, g => g.Count());

        foreach (var s in summaries)
            Assert.Equal(expected[s.Category], s.Count);
    }

    [Fact]
    public async Task GetCategorySummaries_TotalValueMatchesSeedData()
    {
        var summaries = await _client.GetFromJsonAsync<List<CategorySummary>>(
            "/linq/categories/summary", _json);
        Assert.NotNull(summaries);
        var overallExpected = LinqIntermediateService.Products.Sum(p => p.Price);
        var overallActual   = summaries.Sum(s => s.TotalValue);
        Assert.Equal(overallExpected, overallActual);
    }

    // ── Join ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetOrderLines_CountMatchesSeedOrders()
    {
        var lines = await _client.GetFromJsonAsync<List<OrderLine>>(
            "/linq/orders/lines", _json);
        Assert.NotNull(lines);
        Assert.Equal(LinqIntermediateService.Orders.Count, lines.Count);
    }

    [Fact]
    public async Task GetOrderLines_LineTotalIsUnitPriceTimesQuantity()
    {
        var lines = await _client.GetFromJsonAsync<List<OrderLine>>(
            "/linq/orders/lines", _json);
        Assert.NotNull(lines);
        Assert.All(lines, l =>
            Assert.Equal(l.UnitPrice * l.Quantity, l.LineTotal));
    }

    // ── SelectMany ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetProductLabels_CountMatchesProductCount()
    {
        var labels = await _client.GetFromJsonAsync<List<string>>(
            "/linq/products/labels", _json);
        Assert.NotNull(labels);
        Assert.Equal(LinqIntermediateService.Products.Count, labels.Count);
    }

    [Fact]
    public async Task GetProductLabels_EachLabelContainsCategoryAndName()
    {
        var labels = await _client.GetFromJsonAsync<List<string>>(
            "/linq/products/labels", _json);
        Assert.NotNull(labels);
        Assert.All(labels, label => Assert.Matches(@"^\[.+\] .+$", label));
    }

    // ── let clause (discounted products) ─────────────────────────────────────

    [Fact]
    public async Task GetDiscounted_AllDiscountedPricesAreBelowMaxPrice()
    {
        const decimal maxPrice = 100m;
        var items = await _client.GetFromJsonAsync<List<DiscountedProduct>>(
            $"/linq/products/discounted?discountRate=0.10&maxDiscountedPrice={maxPrice}", _json);
        Assert.NotNull(items);
        Assert.NotEmpty(items);
        Assert.All(items, d => Assert.True(d.DiscountedPrice < maxPrice));
    }

    [Fact]
    public async Task GetDiscounted_DiscountedPriceEqualsOriginalTimesRate()
    {
        var items = await _client.GetFromJsonAsync<List<DiscountedProduct>>(
            "/linq/products/discounted?discountRate=0.10&maxDiscountedPrice=1000", _json);
        Assert.NotNull(items);
        Assert.All(items, d =>
            Assert.Equal(d.OriginalPrice * 0.90m, d.DiscountedPrice, precision: 4));
    }

    // ── Chaining ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetTopOrders_Top2_ReturnsExactly2()
    {
        var lines = await _client.GetFromJsonAsync<List<OrderLine>>(
            "/linq/orders/top?topN=2", _json);
        Assert.NotNull(lines);
        Assert.Equal(2, lines.Count);
    }

    [Fact]
    public async Task GetTopOrders_AreOrderedByLineTotalDescending()
    {
        var lines = await _client.GetFromJsonAsync<List<OrderLine>>(
            "/linq/orders/top?topN=5", _json);
        Assert.NotNull(lines);
        for (int i = 1; i < lines.Count; i++)
            Assert.True(lines[i - 1].LineTotal >= lines[i].LineTotal);
    }
}
