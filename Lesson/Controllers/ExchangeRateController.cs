using Lesson.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;

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

    // GET /exchangerate/{baseCurrency}
    // -------------------------------------------------------------------------
    // NEW IN LESSON 01-C: [OutputCache(PolicyName = "ExchangeRates")]
    //
    // The "ExchangeRates" policy (defined in Program.cs) caches the full
    // serialized response for 60 seconds. On a cache hit, this method is
    // never invoked — ASP.NET Core returns the stored bytes directly.
    //
    // The cache key is the request URL path, so /exchangerate/EUR and
    // /exchangerate/USD are cached independently.
    //
    // Java parallel:
    //   @Cacheable(value = "exchangeRates", key = "#baseCurrency")
    // -------------------------------------------------------------------------
    [HttpGet("{baseCurrency}")]
    [OutputCache(PolicyName = "ExchangeRates")]
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
            // Polly has already exhausted retries and the circuit may be open.
            // Surface the final failure as 502 Bad Gateway.
            return StatusCode(StatusCodes.Status502BadGateway, new { error = ex.Message });
        }
    }
}
