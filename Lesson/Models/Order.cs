namespace Lesson.Models;

/// <summary>Order placed by a customer for a product.</summary>
public record Order(int Id, int CustomerId, int ProductId, int Quantity);
