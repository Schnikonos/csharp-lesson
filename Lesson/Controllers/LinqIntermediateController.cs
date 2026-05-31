using Lesson.Models;
using Lesson.Services;
using Microsoft.AspNetCore.Mvc;

namespace Lesson.Controllers;

/// <summary>
/// Lesson 05-B — Intermediate LINQ:
///   IEnumerable vs IQueryable, GroupBy, Join, SelectMany, let, anonymous types, chaining.
/// </summary>
[ApiController]
[Route("linq")]
public class LinqIntermediateController(LinqIntermediateService svc) : ControllerBase
{
    // GET /linq/filter-in-memory?category=Electronics
    [HttpGet("filter-in-memory")]
    public ActionResult<IEnumerable<ProductResponse>> FilterInMemory(
        [FromQuery] string category = "Electronics")
        => Ok(svc.FilterInMemory(category).Select(ToResponse));

    // GET /linq/filter-lazy?category=Electronics
    [HttpGet("filter-lazy")]
    public ActionResult<IEnumerable<ProductResponse>> FilterLazy(
        [FromQuery] string category = "Electronics")
        => Ok(svc.FilterLazy(category).Select(ToResponse));

    // GET /linq/categories/summary
    [HttpGet("categories/summary")]
    public ActionResult<IEnumerable<CategorySummary>> GetCategorySummaries()
        => Ok(svc.GetCategorySummaries());

    // GET /linq/orders/lines
    [HttpGet("orders/lines")]
    public ActionResult<IEnumerable<OrderLine>> GetOrderLines()
        => Ok(svc.GetOrderLines());

    // GET /linq/products/labels
    [HttpGet("products/labels")]
    public ActionResult<IEnumerable<string>> GetProductLabels()
        => Ok(svc.GetAllProductLabels());

    // GET /linq/products/discounted?discountRate=0.10&maxDiscountedPrice=50
    [HttpGet("products/discounted")]
    public ActionResult<IEnumerable<DiscountedProduct>> GetDiscounted(
        [FromQuery] decimal discountRate = 0.10m,
        [FromQuery] decimal maxDiscountedPrice = 100m)
        => Ok(svc.GetDiscountedProducts(discountRate, maxDiscountedPrice));

    // GET /linq/orders/top?topN=3
    [HttpGet("orders/top")]
    public ActionResult<IEnumerable<OrderLine>> GetTopOrders(
        [FromQuery] int topN = 3)
        => Ok(svc.GetTopOrderLinesByTotal(topN));

    private static ProductResponse ToResponse(Product p)
        => new(p.Id, p.Name, p.Category, p.Price, p.Stock);
}
