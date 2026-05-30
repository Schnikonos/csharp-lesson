namespace Lesson.Controllers;

// ─── Lesson 05 LINQ DTOs ──────────────────────────────────────────────────────

/// <summary>Lightweight product response (no internal fields exposed).</summary>
public record ProductResponse(
    int     Id,
    string  Name,
    string  Category,
    decimal Price,
    int     Stock
);

/// <summary>Name + price projection — demonstrates Select into a DTO.</summary>
public record NamePriceDto(string Name, decimal Price);
