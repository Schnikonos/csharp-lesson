using Lesson.Extensions;
using Lesson.Models;
using Lesson.Services;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;

namespace Lesson.Services;

/// <summary>
/// Lesson 05-C — Advanced LINQ topics:
///   • Custom extension methods (ProductExtensions)
///   • Aggregate
///   • Zip
///   • Chunk
///   • AsParallel (PLINQ basics)
///   • Expression trees — intro only (build and compile a simple predicate)
///   • IAsyncEnumerable&lt;T&gt; streaming
/// </summary>
public class LinqAdvancedService
{
    private static readonly IReadOnlyList<Product> Products = LinqService.Products;

    // ── Custom extension methods ───────────────────────────────────────────────

    /// <summary>
    /// Uses custom extension methods <c>InStock</c>, <c>PriceAbove</c>, and
    /// <c>MostExpensive</c> to build a composed pipeline.
    ///
    /// Java parallel: static utility helper methods — they work but break the
    /// dot-notation chain.
    /// </summary>
    public IReadOnlyList<Product> GetTopInStockAbovePrice(decimal minPrice, int topN)
        => Products
            .InStock()
            .PriceAbove(minPrice)
            .MostExpensive(topN)
            .ToList();

    // ── Aggregate ─────────────────────────────────────────────────────────────

    /// <summary>
    /// <c>Aggregate</c> is the general-purpose fold / reduce operator.
    ///
    /// Seed form: Aggregate(seed, (acc, element) => newAcc)
    ///
    /// Java parallel: stream().reduce(identity, (acc, e) -> acc + e)
    /// </summary>
    public decimal GetTotalInventoryValue()
        => Products.Aggregate(
            0m,
            (total, p) => total + p.Price * p.Stock);

    /// <summary>
    /// Builds a comma-separated catalogue string using Aggregate — equivalent to
    /// string.Join but demonstrates the accumulator pattern explicitly.
    /// </summary>
    public string GetCatalogueString()
        => Products
            .OrderBy(p => p.Name)
            .Aggregate(
                string.Empty,
                (acc, p) => acc.Length == 0
                    ? p.Name
                    : acc + ", " + p.Name);

    // ── Zip ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// <c>Zip</c> pairs elements from two sequences by index — stops at the
    /// shorter sequence.
    ///
    /// Use case: correlate a sorted-by-price list with a rank sequence.
    ///
    /// Java parallel: IntStream.range(0, Math.min(a.size(), b.size()))
    ///                          .mapToObj(i -> new Pair(a.get(i), b.get(i)))
    /// </summary>
    public IReadOnlyList<RankedProduct> GetRankedByPrice()
    {
        var sorted = Products.OrderByDescending(p => p.Price);
        var ranks  = Enumerable.Range(1, Products.Count); // 1, 2, 3, …
        return sorted
            .Zip(ranks, (p, rank) => new RankedProduct(rank, p.Name, p.Price))
            .ToList();
    }

    // ── Chunk ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// <c>Chunk(size)</c> splits a sequence into fixed-size sub-arrays.
    /// The last chunk may be smaller.
    ///
    /// Introduced in .NET 6. Useful for batch processing.
    ///
    /// Java parallel: Guava Lists.partition(list, size)
    ///                or custom IntStream splitter.
    /// </summary>
    public IReadOnlyList<Product[]> GetProductsInPages(int pageSize)
        => Products.Chunk(pageSize).ToList();

    // ── AsParallel (PLINQ) ────────────────────────────────────────────────────

    /// <summary>
    /// <c>AsParallel()</c> parallelises a LINQ pipeline over the thread pool.
    /// Best for CPU-bound work on large collections.
    ///
    /// Note: for I/O-bound work prefer <c>async/await</c> over PLINQ.
    ///
    /// Java parallel: stream().parallel()
    /// </summary>
    public IReadOnlyList<Product> GetExpensiveParallel(decimal minPrice)
        => Products
            .AsParallel()                        // parallelise
            .Where(p => p.Price > minPrice)      // run on multiple threads
            .OrderBy(p => p.Name)                // re-serialise and sort
            .ToList();

    // ── Expression Trees — intro ──────────────────────────────────────────────

    /// <summary>
    /// Builds an <c>Expression&lt;Func&lt;Product, bool&gt;&gt;</c> at runtime and
    /// compiles it to a delegate.
    ///
    /// This is the foundation of how EF Core, AutoMapper, and dynamic query
    /// builders (e.g. System.Linq.Dynamic.Core) work internally.
    ///
    /// The tree built here is equivalent to: <c>p => p.Price &lt; maxPrice</c>
    ///
    /// Java parallel: no direct equivalent; closest is reflection + Predicate.
    /// </summary>
    public IReadOnlyList<Product> FilterByExpressionTree(decimal maxPrice)
    {
        // Build: p => p.Price < maxPrice
        var param    = Expression.Parameter(typeof(Product), "p");
        var property = Expression.Property(param, nameof(Product.Price));
        var constant = Expression.Constant(maxPrice, typeof(decimal));
        var body     = Expression.LessThan(property, constant);
        var lambda   = Expression.Lambda<Func<Product, bool>>(body, param);

        // Compile to a regular delegate and use it in a LINQ pipeline
        var predicate = lambda.Compile();
        return Products.Where(predicate).OrderBy(p => p.Price).ToList();
    }

    // ── IAsyncEnumerable<T> ───────────────────────────────────────────────────

    /// <summary>
    /// Streams products one at a time using <c>IAsyncEnumerable&lt;T&gt;</c>.
    ///
    /// The caller receives each item as it becomes available without waiting for
    /// the full result set — ideal for large datasets or real-time feeds.
    ///
    /// <c>yield return</c> inside an <c>async</c> iterator produces an
    /// <c>IAsyncEnumerable&lt;T&gt;</c> automatically.
    ///
    /// Java parallel: Project Reactor <c>Flux&lt;T&gt;</c> or
    ///                Java 9+ <c>Flow.Publisher&lt;T&gt;</c> reactive streams.
    /// </summary>
    public async IAsyncEnumerable<Product> StreamProductsAsync(
        decimal maxPrice,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var product in Products.Where(p => p.Price <= maxPrice).OrderBy(p => p.Price))
        {
            await Task.Delay(0, ct); // simulate async source (e.g. DB cursor)
            yield return product;
        }
    }
}
