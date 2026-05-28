using System.Text.Json;
using System.Text.Json.Serialization;
using Lesson.Config;
using Microsoft.Extensions.Options;

namespace Lesson.Services;

// -----------------------------------------------------------------------------
// TYPED HTTP CLIENT PATTERN
//
// A "typed client" is a class that receives an HttpClient injected by
// IHttpClientFactory. This is the recommended approach in ASP.NET Core.
//
// Why NOT "new HttpClient()" directly (the antipattern):
//   - HttpClient holds a socket. Creating one per request exhausts sockets.
//   - Disposing it does NOT release the socket immediately.
//   - IHttpClientFactory manages a pool of HttpMessageHandler instances,
//     respects DNS TTL, and handles lifetime properly.
//
// Java parallel:
//   @Bean WebClient.Builder / RestTemplate  →  IHttpClientFactory typed client
//   new RestTemplate()                      →  new HttpClient()  ← antipattern
//
// HOW IT WORKS:
//   1. Register: builder.Services.AddHttpClient<IExchangeRateService, ExchangeRateService>()
//   2. IHttpClientFactory creates a managed HttpClient and injects it here.
//   3. You use _httpClient directly — no manual lifecycle management needed.
// -----------------------------------------------------------------------------

public class ExchangeRateService : IExchangeRateService
{
    private readonly HttpClient _httpClient;

    // IOptions<T> gives you access to the strongly-typed config section.
    // .Value reads the bound instance. Covered in depth in Lesson 02.
    public ExchangeRateService(HttpClient httpClient, IOptions<ExchangeRateOptions> options)
    {
        _httpClient = httpClient;

        // Configure the base address once at construction — all requests
        // made with this client are relative to this URL.
        _httpClient.BaseAddress = new Uri(options.Value.BaseUrl);
        _httpClient.Timeout = TimeSpan.FromSeconds(options.Value.TimeoutSeconds);
    }

    public async Task<Dictionary<string, decimal>> GetRatesAsync(
        string baseCurrency,
        CancellationToken cancellationToken = default)
    {
        // ---------------------------------------------------------------------
        // C# NOTE: async / await
        //
        // "await" suspends this method and returns the thread to the thread pool
        // while the I/O is in flight. When the response arrives, execution
        // resumes — potentially on a different thread pool thread.
        //
        // Java parallel:
        //   CompletableFuture.supplyAsync(...)  →  async Task (state machine)
        //   .thenApply(...)                     →  code after each "await"
        //
        // ConfigureAwait(false):
        //   In ASP.NET Core you do NOT need ConfigureAwait(false) — there is no
        //   SynchronizationContext to deadlock on (unlike WinForms/WPF/ASP.NET
        //   Classic). It is still sometimes used in library code as a best
        //   practice, but for application code it makes no difference here.
        // ---------------------------------------------------------------------

        // GetAsync sends HTTP GET and passes the CancellationToken so the
        // request is aborted if the client disconnects or the timeout fires.
        var response = await _httpClient.GetAsync(baseCurrency, cancellationToken);

        // EnsureSuccessStatusCode throws HttpRequestException for 4xx/5xx.
        // Java parallel: if (!response.isSuccessful()) throw new RuntimeException(...)
        response.EnsureSuccessStatusCode();

        // ReadFromJsonAsync deserializes the response body using System.Text.Json
        // (built-in, no external library needed).
        // Java parallel: objectMapper.readValue(body, ExchangeRateResponse.class)
        var result = await response.Content
            .ReadFromJsonAsync<ExchangeRateApiResponse>(cancellationToken: cancellationToken);

        return result?.Rates ?? [];
    }

    // -------------------------------------------------------------------------
    // Private DTO for deserializing the external API response.
    // Kept private — it is an implementation detail of this service.
    // The API returns: { "result": "success", "rates": { "USD": 1.08, ... } }
    // -------------------------------------------------------------------------
    private sealed class ExchangeRateApiResponse
    {
        [JsonPropertyName("rates")]
        public Dictionary<string, decimal> Rates { get; init; } = [];
    }
}
