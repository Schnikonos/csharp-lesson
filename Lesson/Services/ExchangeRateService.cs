using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lesson.Services;

// -----------------------------------------------------------------------------
// LESSON 01-C: SIMPLIFIED TYPED CLIENT
//
// In 01-B the service constructor set BaseAddress and Timeout on the HttpClient
// using injected IOptions<ExchangeRateOptions>. That mixed two concerns:
//   1. HTTP client configuration  (how to connect)
//   2. Business logic             (what to call)
//
// In 01-C those configuration concerns move into Program.cs — specifically into
// the AddHttpClient fluent builder, which is the idiomatic ASP.NET Core place
// for client setup. The service class now has a single dependency: HttpClient.
//
// Java parallel:
//   Configuring a WebClient bean (base URL, timeouts, filters) in @Configuration
//   rather than inside @Service — the @Configuration approach is preferred for
//   the same reasons (separation of concerns, easier testing).
// -----------------------------------------------------------------------------

public class ExchangeRateService : IExchangeRateService
{
    private readonly HttpClient _httpClient;

    // IOptions<ExchangeRateOptions> is no longer needed here.
    // BaseAddress and Timeout are configured by the DI builder in Program.cs.
    public ExchangeRateService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<Dictionary<string, decimal>> GetRatesAsync(
        string baseCurrency,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync(baseCurrency, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content
            .ReadFromJsonAsync<ExchangeRateApiResponse>(cancellationToken: cancellationToken);

        return result?.Rates ?? [];
    }

    // Private DTO — implementation detail of this service.
    // The API returns: { "result": "success", "rates": { "USD": 1.08, ... } }
    private sealed class ExchangeRateApiResponse
    {
        [JsonPropertyName("rates")]
        public Dictionary<string, decimal> Rates { get; init; } = [];
    }
}
