namespace Lesson.Models;

// ─── Lesson 05-B result models ────────────────────────────────────────────────

/// <summary>Grouped category statistics.</summary>
public record CategorySummary(
    string  Category,
    int     Count,
    decimal TotalValue,
    double  AveragePrice
);

/// <summary>Joined order + product result.</summary>
public record OrderLine(
    int     OrderId,
    int     CustomerId,
    string  ProductName,
    string  Category,
    decimal UnitPrice,
    int     Quantity,
    decimal LineTotal
);

/// <summary>Product with a computed discounted price (let clause demo).</summary>
public record DiscountedProduct(
    string  Name,
    decimal OriginalPrice,
    decimal DiscountedPrice
);
