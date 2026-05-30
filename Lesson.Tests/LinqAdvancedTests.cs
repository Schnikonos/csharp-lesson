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
/// Lesson 05-C integration tests — verifies:
///   • Custom LINQ extension methods (InStock, PriceAbove, MostExpensive)
///   • Aggregate (sum, string fold)
///   • Zip (ranked products)
///   • Chunk (pages)
///   • AsParallel (same results as sequential)
///   • Expression trees (filter compiled at runtime)
///   • IAsyncEnumerable&lt;T&gt; (streaming endpoint)
/// </summary>
public class LinqAdvancedTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly SqliteConnection _connection;
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public LinqAdvancedTests(WebApplicationFactory<Program> factory)
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

    // ── Custom extension methods ───────────────────────────────────────────────

    [Fact]
    public async Task GetTopInStock_ReturnsAtMostTopN()
    {
        var products = await _client.GetFromJsonAsync<List<ProductResponse>>(
            "/linq/advanced/top-in-stock?minPrice=0&topN=3", _json);
        Assert.NotNull(products);
        Assert.Equal(3, products.Count);
    }

    [Fact]
    public async Task GetTopInStock_AllProductsExceedMinPrice()
    {
        const decimal minPrice = 100m;
        var products = await _client.GetFromJsonAsync<List<ProductResponse>>(
            $"/linq/advanced/top-in-stock?minPrice={minPrice}&topN=10", _json);
        Assert.NotNull(products);
        Assert.All(products, p => Assert.True(p.Price >= minPrice));
    }

    // ── Aggregate ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetInventoryValue_MatchesManualCalculation()
    {
        var expected = LinqService.Products.Sum(p => p.Price * p.Stock);
        var actual   = await _client.GetFromJsonAsync<decimal>(
            "/linq/advanced/inventory-value", _json);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task GetCatalogue_ContainsAllProductNames()
    {
        var result = await _client.GetFromJsonAsync<CatalogueResponse>(
            "/linq/advanced/catalogue", _json);
        Assert.NotNull(result);
        foreach (var product in LinqService.Products)
            Assert.Contains(product.Name, result.Value);
    }

    private record CatalogueResponse(string Value);

    // ── Zip ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetRanked_CountMatchesProductCount()
    {
        var ranked = await _client.GetFromJsonAsync<List<RankedProduct>>(
            "/linq/advanced/ranked", _json);
        Assert.NotNull(ranked);
        Assert.Equal(LinqService.Products.Count, ranked.Count);
    }

    [Fact]
    public async Task GetRanked_Rank1HasHighestPrice()
    {
        var ranked = await _client.GetFromJsonAsync<List<RankedProduct>>(
            "/linq/advanced/ranked", _json);
        Assert.NotNull(ranked);
        var rank1 = ranked.Single(r => r.Rank == 1);
        Assert.Equal(LinqService.Products.Max(p => p.Price), rank1.Price);
    }

    // ── Chunk ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetChunks_PageSize3_ProducesCorrectNumberOfChunks()
    {
        var chunks = await _client.GetFromJsonAsync<List<List<ProductResponse>>>(
            "/linq/advanced/chunks?pageSize=3", _json);
        Assert.NotNull(chunks);
        int expectedChunks = (int)Math.Ceiling(LinqService.Products.Count / 3.0);
        Assert.Equal(expectedChunks, chunks.Count);
    }

    [Fact]
    public async Task GetChunks_TotalItemsEqualsProductCount()
    {
        var chunks = await _client.GetFromJsonAsync<List<List<ProductResponse>>>(
            "/linq/advanced/chunks?pageSize=4", _json);
        Assert.NotNull(chunks);
        Assert.Equal(LinqService.Products.Count, chunks.Sum(c => c.Count));
    }

    // ── AsParallel ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetParallel_SameIdsAsSequentialFilter()
    {
        const decimal minPrice = 50m;
        var parallel = await _client.GetFromJsonAsync<List<ProductResponse>>(
            $"/linq/advanced/parallel?minPrice={minPrice}", _json);
        var expected = LinqService.Products
            .Where(p => p.Price > minPrice)
            .Select(p => p.Id)
            .Order()
            .ToList();
        Assert.NotNull(parallel);
        Assert.Equal(expected, parallel.Select(p => p.Id).Order().ToList());
    }

    // ── Expression Trees ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetByExpressionTree_AllProductsBelowMaxPrice()
    {
        const decimal maxPrice = 100m;
        var products = await _client.GetFromJsonAsync<List<ProductResponse>>(
            $"/linq/advanced/expression-tree?maxPrice={maxPrice}", _json);
        Assert.NotNull(products);
        Assert.NotEmpty(products);
        Assert.All(products, p => Assert.True(p.Price < maxPrice));
    }

    [Fact]
    public async Task GetByExpressionTree_SameResultAsDirectFilter()
    {
        const decimal maxPrice = 200m;
        var viaTree = await _client.GetFromJsonAsync<List<ProductResponse>>(
            $"/linq/advanced/expression-tree?maxPrice={maxPrice}", _json);
        var expected = LinqService.Products
            .Where(p => p.Price < maxPrice)
            .Select(p => p.Id)
            .Order()
            .ToList();
        Assert.NotNull(viaTree);
        Assert.Equal(expected, viaTree.Select(p => p.Id).Order().ToList());
    }

    // ── IAsyncEnumerable ──────────────────────────────────────────────────────

    [Fact]
    public async Task StreamProducts_AllNamesAreBelowMaxPrice()
    {
        const decimal maxPrice = 100m;
        var names = await _client.GetFromJsonAsync<List<string>>(
            $"/linq/advanced/stream?maxPrice={maxPrice}", _json);
        Assert.NotNull(names);
        Assert.NotEmpty(names);
        var validNames = LinqService.Products
            .Where(p => p.Price <= maxPrice)
            .Select(p => p.Name)
            .ToHashSet();
        Assert.All(names, n => Assert.Contains(n, validNames));
    }

    [Fact]
    public async Task StreamProducts_CountMatchesExpected()
    {
        const decimal maxPrice = 50m;
        var names = await _client.GetFromJsonAsync<List<string>>(
            $"/linq/advanced/stream?maxPrice={maxPrice}", _json);
        var expected = LinqService.Products.Count(p => p.Price <= maxPrice);
        Assert.NotNull(names);
        Assert.Equal(expected, names.Count);
    }
}
