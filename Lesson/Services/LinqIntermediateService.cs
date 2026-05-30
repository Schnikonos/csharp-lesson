using Lesson.Models;

namespace Lesson.Services;

/// <summary>
/// Lesson 05-B — Intermediate LINQ over in-memory collections.
///
/// Topics:
///   • IEnumerable&lt;T&gt; vs IQueryable&lt;T&gt; (conceptual; demonstrated with in-memory data)
///   • GroupBy
///   • Join
///   • SelectMany (flattening)
///   • Pipeline chaining
///   • let clause (query syntax)
///   • Anonymous types
///
/// Java parallels noted per method.
/// </summary>
public class LinqIntermediateService
{
    // ── Extra seed data for join / SelectMany demos ───────────────────────────

    public static readonly IReadOnlyList<Product> Products = LinqService.Products;

    public static readonly IReadOnlyList<Order> Orders = new List<Order>
    {
        new(1, CustomerId: 10, ProductId: 1, Quantity: 2),   // 2× Laptop Pro
        new(2, CustomerId: 10, ProductId: 6, Quantity: 5),   // 5× Notebook A5
        new(3, CustomerId: 11, ProductId: 2, Quantity: 1),   // 1× Wireless Mouse
        new(4, CustomerId: 11, ProductId: 8, Quantity: 1),   // 1× Monitor 27"
        new(5, CustomerId: 12, ProductId: 4, Quantity: 1),   // 1× Standing Desk
        new(6, CustomerId: 12, ProductId: 7, Quantity: 10),  // 10× Ballpoint Pens
        new(7, CustomerId: 13, ProductId: 3, Quantity: 3),   // 3× USB-C Hub
    };

    // ── IEnumerable vs IQueryable (conceptual demo) ───────────────────────────

    /// <summary>
    /// Demonstrates the IEnumerable path: <c>ToList()</c> is called first,
    /// then the filter runs in C# memory over the resulting List.
    ///
    /// With EF Core / IQueryable the filter would have been part of the SQL WHERE clause.
    /// Here we simply show the difference in call order using static data.
    ///
    /// Java parallel: calling .collect(toList()) before .filter() forces eager evaluation.
    /// </summary>
    public IReadOnlyList<Product> FilterInMemory(string category)
    {
        // Simulate IEnumerable path: materialise first, then filter
        IEnumerable<Product> inMemory = Products.ToList();   // all 10 loaded
        return inMemory.Where(p => p.Category == category).ToList();
    }

    /// <summary>
    /// Demonstrates the IQueryable-equivalent path: filter is composed before
    /// materialisation — only matching elements are ever processed.
    ///
    /// Over a real DbSet this would produce SQL: WHERE Category = @category.
    ///
    /// Java parallel: JpaSpecificationExecutor builds a lazy Specification; calling
    /// findAll(spec) executes it against the database.
    /// </summary>
    public IReadOnlyList<Product> FilterLazy(string category)
    {
        IEnumerable<Product> lazy = Products;                // no materialisation yet
        lazy = lazy.Where(p => p.Category == category);      // deferred
        return lazy.ToList();                                // materialise once
    }

    // ── GroupBy ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Groups products by category and projects each group into a summary.
    ///
    /// Java parallel:
    ///   stream().collect(Collectors.groupingBy(Product::getCategory,
    ///       Collectors.summarizingDouble(Product::getPrice)))
    /// </summary>
    public IReadOnlyList<CategorySummary> GetCategorySummaries()
        => Products
            .GroupBy(p => p.Category)
            .Select(g => new CategorySummary(
                Category:     g.Key,
                Count:        g.Count(),
                TotalValue:   g.Sum(p => p.Price),
                AveragePrice: g.Average(p => (double)p.Price)))
            .OrderBy(s => s.Category)
            .ToList();

    // ── Join ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Joins Orders to Products to produce a flat order-line result.
    ///
    /// LINQ Join signature:
    ///   outer.Join(inner, outerKey, innerKey, resultSelector)
    ///
    /// Java parallel:
    ///   orders.stream()
    ///         .flatMap(o -> products.stream()
    ///             .filter(p -> p.getId() == o.getProductId())
    ///             .map(p -> new OrderLine(o, p)))
    ///         .collect(toList())
    ///   — or — use a Map lookup for O(1) join.
    /// </summary>
    public IReadOnlyList<OrderLine> GetOrderLines()
        => Orders
            .Join(
                Products,
                o => o.ProductId,          // outer key
                p => p.Id,                 // inner key
                (o, p) => new OrderLine(
                    OrderId:      o.Id,
                    CustomerId:   o.CustomerId,
                    ProductName:  p.Name,
                    Category:     p.Category,
                    UnitPrice:    p.Price,
                    Quantity:     o.Quantity,
                    LineTotal:    p.Price * o.Quantity))
            .ToList();

    // ── SelectMany ────────────────────────────────────────────────────────────

    /// <summary>
    /// SelectMany flattens a sequence of sequences into a single sequence.
    ///
    /// Here: group products by category, then flatten each group's product names
    /// into a single list prefixed with the category.
    ///
    /// Java parallel:
    ///   categories.stream().flatMap(c -> c.getProducts().stream().map(...))
    /// </summary>
    public IReadOnlyList<string> GetAllProductLabels()
        => Products
            .GroupBy(p => p.Category)
            .OrderBy(g => g.Key)
            .SelectMany(g => g.Select(p => $"[{g.Key}] {p.Name}"))
            .ToList();

    // ── let clause (query syntax) ─────────────────────────────────────────────

    /// <summary>
    /// The <c>let</c> clause introduces an intermediate variable inside a query-syntax
    /// expression — equivalent to a <c>Select</c> that carries extra computed data.
    ///
    /// Here: compute the discounted price once and use it in both the where and select.
    ///
    /// Java parallel: a multi-step stream with a temporary record/DTO:
    ///   stream().map(p -> new PriceHolder(p, p.getPrice() * 0.9))
    ///           .filter(h -> h.discounted() < threshold)
    ///           .map(h -> new DiscountedProduct(h.product().getName(), h.discounted()))
    /// </summary>
    public IReadOnlyList<DiscountedProduct> GetDiscountedProducts(
        decimal discountRate, decimal maxDiscountedPrice)
        => (from p in Products
            let discounted = p.Price * (1 - discountRate)
            where discounted < maxDiscountedPrice
            orderby discounted
            select new DiscountedProduct(p.Name, p.Price, discounted)).ToList();

    // ── Chaining ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Demonstrates combining multiple operators in a single readable pipeline.
    /// Java parallel: a multi-step stream chain.
    /// </summary>
    public IReadOnlyList<OrderLine> GetTopOrderLinesByTotal(int topN)
        => GetOrderLines()
            .OrderByDescending(ol => ol.LineTotal)
            .Take(topN)
            .ToList();
}
