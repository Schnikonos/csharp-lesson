using Lesson.Services;
using Microsoft.AspNetCore.Mvc;

namespace Lesson.Controllers;

[ApiController]
[Route("[controller]")]
public class ExchangeRateController : ControllerBase
{
    private readonly IExchangeRateService _exchangeRateService;

    public ExchangeRateController(IExchangeRateService exchangeRateService)
    {
        _exchangeRateService = exchangeRateService;
    }

    // GET /exchangerate/{baseCurrency}  e.g. GET /exchangerate/EUR
    // -------------------------------------------------------------------------
    // C# NOTE: CancellationToken as an action parameter
    //
    // ASP.NET Core automatically binds a CancellationToken parameter to the
    // HTTP request's cancellation token. You do NOT annotate it — the framework
    // detects it by type.
    //
    // This means: if the client closes the connection mid-request, the token
    // is cancelled, the await in ExchangeRateService is interrupted, and the
    // outgoing HTTP call to the external API is aborted — no wasted resources.
    //
    // Java parallel:
    //   Spring MVC has no direct equivalent; you would use DeferredResult or
    //   reactive WebFlux to achieve the same cancellation propagation.
    // -------------------------------------------------------------------------
    [HttpGet("{baseCurrency}")]
    [ProducesResponseType<Dictionary<string, decimal>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> GetRates(string baseCurrency, CancellationToken cancellationToken)
    {
        try
        {
            var rates = await _exchangeRateService.GetRatesAsync(
                baseCurrency.ToUpperInvariant(),
                cancellationToken);

            return Ok(rates);
        }
        catch (HttpRequestException ex)
        {
            // The external API returned an error — surface it as 502 Bad Gateway,
            // which is the correct HTTP status when an upstream dependency fails.
            return StatusCode(StatusCodes.Status502BadGateway, new { error = ex.Message });
        }
    }
}
