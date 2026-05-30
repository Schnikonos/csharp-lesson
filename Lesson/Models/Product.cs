namespace Lesson.Models;

/// <summary>
/// Simple in-memory model used across all Lesson 05 LINQ demos.
/// No EF Core / database involved — pure C# collections.
/// </summary>
public record Product(
    int     Id,
    string  Name,
    string  Category,
    decimal Price,
    int     Stock
);
