using Lesson.Services;
using Microsoft.AspNetCore.Mvc;

namespace Lesson.Controllers;

/// <summary>
/// Lesson 05-A — LINQ basics: Where, Select, OrderBy, FirstOrDefault, deferred execution.
///
/// All endpoints operate on LinqService.Products (static in-memory list).
/// No database involved — pure LINQ over IEnumerable&lt;T&gt;.
/// </summary>
[ApiController]
[Route("linq")]
public class LinqController(LinqService svc) : ControllerBase
{
    // GET /linq/products?category=Electronics
    [HttpGet("products")]
    public ActionResult<IEnumerable<ProductResponse>> GetByCategory(
        [FromQuery] string? category = null)
    {
        var items = category is null
            ? LinqService.Products
            : svc.FilterByCategory(category);
        return Ok(items.Select(ToResponse));
    }

    // GET /linq/products/query-syntax?category=Furniture
    [HttpGet("products/query-syntax")]
    public ActionResult<IEnumerable<ProductResponse>> GetByCategoryQuerySyntax(
        [FromQuery] string category = "Electronics")
        => Ok(svc.FilterByCategoryQuerySyntax(category).Select(ToResponse));

    // GET /linq/products/name-price
    [HttpGet("products/name-price")]
    public ActionResult<IEnumerable<NamePriceDto>> GetNameAndPrice()
        => Ok(svc.GetNameAndPrice().Select(t => new NamePriceDto(t.Name, t.Price)));

    // GET /linq/products/by-price-desc
    [HttpGet("products/by-price-desc")]
    public ActionResult<IEnumerable<ProductResponse>> GetByPriceDescending()
        => Ok(svc.GetByPriceDescending().Select(ToResponse));

    // GET /linq/products/{id}
    [HttpGet("products/{id:int}")]
    public ActionResult<ProductResponse> GetById(int id)
    {
        var product = svc.FindById(id);
        if (product is null) return NotFound(new { Error = $"Product {id} not found." });
        return Ok(ToResponse(product));
    }

    // GET /linq/products/affordable?maxPrice=50
    [HttpGet("products/affordable")]
    public ActionResult<IEnumerable<string>> GetAffordableNames(
        [FromQuery] decimal maxPrice = 50m)
        => Ok(svc.GetAffordableProductNames(maxPrice));

    private static ProductResponse ToResponse(Lesson.Models.Product p)
        => new(p.Id, p.Name, p.Category, p.Price, p.Stock);
}
