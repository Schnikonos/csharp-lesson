using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lesson.Controllers;
using Lesson.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lesson.Tests;

/// <summary>
/// Lesson 05-A integration tests — verifies:
///   • Where (FilterByCategory / query syntax)
///   • Select projection (NamePriceDto)
///   • OrderByDescending + ThenBy
///   • FirstOrDefault (FindById)
///   • Deferred execution pipeline (GetAffordableProductNames)
///
/// The test host needs a valid SQLite connection so Program.cs can call
/// Database.Migrate() at startup without throwing.
/// </summary>
public class LinqBasicTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly SqliteConnection _connection;
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public LinqBasicTests(WebApplicationFactory<Program> factory)
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

    // ── Where ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task FilterByCategory_Electronics_ReturnsOnlyElectronics()
    {
        var products = await _client.GetFromJsonAsync<List<ProductResponse>>(
            "/linq/products?category=Electronics", _json);
        Assert.NotNull(products);
        Assert.NotEmpty(products);
        Assert.All(products, p => Assert.Equal("Electronics", p.Category));
    }

    [Fact]
    public async Task FilterByCategory_UnknownCategory_ReturnsEmpty()
    {
        var products = await _client.GetFromJsonAsync<List<ProductResponse>>(
            "/linq/products?category=Unknown", _json);
        Assert.NotNull(products);
        Assert.Empty(products);
    }

    [Fact]
    public async Task FilterByCategory_NoCategory_ReturnsAll()
    {
        var products = await _client.GetFromJsonAsync<List<ProductResponse>>(
            "/linq/products", _json);
        Assert.NotNull(products);
        Assert.Equal(10, products.Count);
    }

    // ── Query syntax ──────────────────────────────────────────────────────────

    [Fact]
    public async Task QuerySyntax_SameResultAsMethodSyntax()
    {
        var method = await _client.GetFromJsonAsync<List<ProductResponse>>(
            "/linq/products?category=Furniture", _json);
        var query = await _client.GetFromJsonAsync<List<ProductResponse>>(
            "/linq/products/query-syntax?category=Furniture", _json);
        Assert.NotNull(method);
        Assert.NotNull(query);
        Assert.Equal(method.Select(p => p.Id).Order(),
                     query.Select(p => p.Id).Order());
    }

    // ── Select projection ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetNameAndPrice_ReturnsOnlyNameAndPrice()
    {
        var items = await _client.GetFromJsonAsync<List<NamePriceDto>>(
            "/linq/products/name-price", _json);
        Assert.NotNull(items);
        Assert.Equal(10, items.Count);
        Assert.All(items, item =>
        {
            Assert.False(string.IsNullOrWhiteSpace(item.Name));
            Assert.True(item.Price > 0);
        });
    }

    // ── OrderBy ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetByPriceDescending_FirstItemIsHighestPrice()
    {
        var products = await _client.GetFromJsonAsync<List<ProductResponse>>(
            "/linq/products/by-price-desc", _json);
        Assert.NotNull(products);
        Assert.True(products.Count > 1);
        Assert.Equal(products.Max(p => p.Price), products[0].Price);
    }

    [Fact]
    public async Task GetByPriceDescending_IsSorted()
    {
        var products = await _client.GetFromJsonAsync<List<ProductResponse>>(
            "/linq/products/by-price-desc", _json);
        Assert.NotNull(products);
        for (int i = 1; i < products.Count; i++)
            Assert.True(products[i - 1].Price >= products[i].Price);
    }

    // ── FirstOrDefault ────────────────────────────────────────────────────────

    [Fact]
    public async Task FindById_ExistingId_ReturnsProduct()
    {
        var product = await _client.GetFromJsonAsync<ProductResponse>(
            "/linq/products/1", _json);
        Assert.NotNull(product);
        Assert.Equal(1, product.Id);
    }

    [Fact]
    public async Task FindById_MissingId_ReturnsNotFound()
    {
        var response = await _client.GetAsync("/linq/products/999");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── Deferred execution ────────────────────────────────────────────────────

    [Fact]
    public async Task GetAffordableNames_MaxPrice50_OnlyCheapProducts()
    {
        var names = await _client.GetFromJsonAsync<List<string>>(
            "/linq/products/affordable?maxPrice=50", _json);
        Assert.NotNull(names);
        Assert.NotEmpty(names);
        var expected = Lesson.Services.LinqService.Products
            .Where(p => p.Price <= 50)
            .OrderBy(p => p.Price)
            .Select(p => p.Name)
            .ToList();
        Assert.Equal(expected, names);
    }
}
