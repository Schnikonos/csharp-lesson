using Lesson.Models;
using Lesson.Services;
using Microsoft.AspNetCore.Mvc;

namespace Lesson.Controllers;

/// <summary>
/// Lesson 05-C — Advanced LINQ:
///   custom extensions, Aggregate, Zip, Chunk, AsParallel, expression trees, IAsyncEnumerable.
/// </summary>
[ApiController]
[Route("linq/advanced")]
public class LinqAdvancedController(LinqAdvancedService svc) : ControllerBase
{
    // GET /linq/advanced/top-in-stock?minPrice=50&topN=3
    [HttpGet("top-in-stock")]
    public ActionResult<IEnumerable<ProductResponse>> GetTopInStock(
        [FromQuery] decimal minPrice = 50m,
        [FromQuery] int     topN     = 3)
        => Ok(svc.GetTopInStockAbovePrice(minPrice, topN).Select(ToResponse));

    // GET /linq/advanced/inventory-value
    [HttpGet("inventory-value")]
    public ActionResult<decimal> GetInventoryValue()
        => Ok(svc.GetTotalInventoryValue());

    // GET /linq/advanced/catalogue
    [HttpGet("catalogue")]
    public IActionResult GetCatalogue()
        => Ok(new { value = svc.GetCatalogueString() });

    // GET /linq/advanced/ranked
    [HttpGet("ranked")]
    public ActionResult<IEnumerable<RankedProduct>> GetRanked()
        => Ok(svc.GetRankedByPrice());

    // GET /linq/advanced/chunks?pageSize=3
    [HttpGet("chunks")]
    public ActionResult<IEnumerable<IEnumerable<ProductResponse>>> GetChunks(
        [FromQuery] int pageSize = 3)
        => Ok(svc.GetProductsInPages(pageSize)
                 .Select(page => page.Select(ToResponse)));

    // GET /linq/advanced/parallel?minPrice=50
    [HttpGet("parallel")]
    public ActionResult<IEnumerable<ProductResponse>> GetParallel(
        [FromQuery] decimal minPrice = 50m)
        => Ok(svc.GetExpensiveParallel(minPrice).Select(ToResponse));

    // GET /linq/advanced/expression-tree?maxPrice=100
    [HttpGet("expression-tree")]
    public ActionResult<IEnumerable<ProductResponse>> GetByExpressionTree(
        [FromQuery] decimal maxPrice = 100m)
        => Ok(svc.FilterByExpressionTree(maxPrice).Select(ToResponse));

    // GET /linq/advanced/stream?maxPrice=100
    // Returns newline-delimited product names (simulates streaming)
    [HttpGet("stream")]
    public async Task<IActionResult> StreamProducts(
        [FromQuery] decimal maxPrice = 100m,
        CancellationToken ct = default)
    {
        var names = new List<string>();
        await foreach (var p in svc.StreamProductsAsync(maxPrice, ct))
            names.Add(p.Name);
        return Ok(names);
    }

    private static ProductResponse ToResponse(Product p)
        => new(p.Id, p.Name, p.Category, p.Price, p.Stock);
}
