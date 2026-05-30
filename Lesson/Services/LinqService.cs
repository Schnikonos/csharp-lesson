using Lesson.Models;

namespace Lesson.Services;

/// <summary>
/// Lesson 05-A — LINQ basics over in-memory collections.
///
/// Topics:
///   • Method syntax vs query syntax
///   • Where, Select, OrderBy / OrderByDescending, FirstOrDefault, ToList
///   • Deferred execution explained
///
/// Java parallel: java.util.stream.Stream
///   stream().filter(...).map(...).sorted(...).collect(toList())
/// </summary>
public class LinqService
{
    // ── Sample data ───────────────────────────────────────────────────────────
    // Static seed list — represents an in-memory "database".
    // Every method starts from this list so results are deterministic.
    public static readonly IReadOnlyList<Product> Products = new List<Product>
    {
        new(1,  "Laptop Pro",      "Electronics", 1_299.99m, 15),
        new(2,  "Wireless Mouse",  "Electronics",    29.99m, 200),
        new(3,  "USB-C Hub",       "Electronics",    49.99m, 80),
        new(4,  "Standing Desk",   "Furniture",     599.00m, 10),
        new(5,  "Ergonomic Chair", "Furniture",     349.00m, 25),
        new(6,  "Notebook A5",     "Stationery",      4.99m, 500),
        new(7,  "Ballpoint Pens",  "Stationery",      2.49m, 1000),
        new(8,  "Monitor 27\"",    "Electronics",   399.00m, 30),
        new(9,  "Desk Lamp",       "Furniture",      39.99m, 60),
        new(10, "Sticky Notes",    "Stationery",      3.99m, 750),
    };

    // ── 05-A methods ──────────────────────────────────────────────────────────

    /// <summary>
    /// Filter with Where — method syntax.
    ///
    /// Deferred execution: the lambda is NOT evaluated here.
    /// SQL (IQueryable) equivalent would build a WHERE clause.
    /// Evaluation happens at ToList() — the terminal operator.
    ///
    /// Java: stream().filter(p -> p.getCategory().equals(category)).collect(toList())
    /// </summary>
    public IReadOnlyList<Product> FilterByCategory(string category)
        => Products
            .Where(p => p.Category == category)
            .ToList();

    /// <summary>
    /// Same filter using LINQ query syntax (SQL-like).
    /// Both syntaxes compile to identical IL — choose whichever reads better.
    ///
    /// Java: no direct query-syntax equivalent; Java uses method chains only.
    /// </summary>
    public IReadOnlyList<Product> FilterByCategoryQuerySyntax(string category)
        => (from p in Products
            where p.Category == category
            select p).ToList();

    /// <summary>
    /// Project with Select — transform each element into a new shape.
    /// Here: Product → anonymous type with Name + Price.
    ///
    /// Java: stream().map(p -> new NamePrice(p.getName(), p.getPrice())).collect(toList())
    /// </summary>
    public IReadOnlyList<(string Name, decimal Price)> GetNameAndPrice()
        => Products
            .Select(p => (p.Name, p.Price))
            .ToList();

    /// <summary>
    /// Sort with OrderBy / OrderByDescending.
    /// ThenBy adds a secondary sort key.
    ///
    /// Java: stream().sorted(Comparator.comparing(Product::getPrice).reversed())
    /// </summary>
    public IReadOnlyList<Product> GetByPriceDescending()
        => Products
            .OrderByDescending(p => p.Price)
            .ThenBy(p => p.Name)
            .ToList();

    /// <summary>
    /// FirstOrDefault — returns the first match or null (default for reference types).
    /// Never throws; safe to use without a try/catch.
    ///
    /// First() throws InvalidOperationException if no match — prefer FirstOrDefault.
    ///
    /// Java: stream().filter(...).findFirst().orElse(null)
    /// </summary>
    public Product? FindById(int id)
        => Products.FirstOrDefault(p => p.Id == id);

    /// <summary>
    /// Demonstrates deferred execution: building a query pipeline does not
    /// enumerate the source. Materialisation happens at the terminal operator.
    ///
    /// Steps:
    ///   1. Where(…)   → IEnumerable (no iteration yet)
    ///   2. Select(…)  → IEnumerable (no iteration yet)
    ///   3. ToList()   → iterates ONCE, produces List&lt;T&gt;
    ///
    /// Java parallel: Stream is also lazy — only terminal ops (collect, findFirst…)
    /// trigger evaluation.
    /// </summary>
    public IReadOnlyList<string> GetAffordableProductNames(decimal maxPrice)
        => Products
            .Where(p => p.Price <= maxPrice)     // deferred
            .OrderBy(p => p.Price)               // deferred
            .Select(p => p.Name)                 // deferred
            .ToList();                           // ← terminal: evaluates the whole pipeline
}
