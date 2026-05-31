using Lesson.Models;

namespace Lesson.Extensions;

/// <summary>
/// Lesson 05-C — Custom LINQ extension methods.
///
/// Extension methods on IEnumerable&lt;T&gt; let you build reusable, composable
/// pipeline steps that look and feel like built-in LINQ operators.
///
/// Java parallel: static utility methods (e.g. StreamUtils) — they work but
/// do not chain naturally; C# extension methods integrate directly into the
/// dot-notation pipeline.
/// </summary>
public static class ProductExtensions
{
    /// <summary>
    /// Returns only products whose stock is above <paramref name="minStock"/>.
    /// Custom operator that composes with any other LINQ operators.
    /// </summary>
    public static IEnumerable<Product> InStock(
        this IEnumerable<Product> source, int minStock = 1)
        => source.Where(p => p.Stock >= minStock);

    /// <summary>
    /// Returns the <paramref name="n"/> cheapest products.
    /// </summary>
    public static IEnumerable<Product> Cheapest(
        this IEnumerable<Product> source, int n)
        => source.OrderBy(p => p.Price).Take(n);

    /// <summary>
    /// Returns the <paramref name="n"/> most expensive products.
    /// </summary>
    public static IEnumerable<Product> MostExpensive(
        this IEnumerable<Product> source, int n)
        => source.OrderByDescending(p => p.Price).Take(n);

    /// <summary>
    /// Filters by a minimum price threshold (inclusive).
    /// </summary>
    public static IEnumerable<Product> PriceAbove(
        this IEnumerable<Product> source, decimal minPrice)
        => source.Where(p => p.Price >= minPrice);
}
