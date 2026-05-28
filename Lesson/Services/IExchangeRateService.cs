namespace Lesson.Services;

// -----------------------------------------------------------------------------
// C# NOTE: async methods return Task<T> instead of T.
//
// Java parallel:
//   CompletableFuture<Map<String, Double>>  →  Task<Dictionary<string, decimal>>
//
// CancellationToken is the C# equivalent of passing a timeout or interrupt
// signal. It flows from the HTTP request (via the controller) down through
// every async call, allowing the entire chain to be cancelled if the client
// disconnects or a timeout is hit.
//
// Convention: CancellationToken is always the last parameter, named "cancellationToken".
// -----------------------------------------------------------------------------

public interface IExchangeRateService
{
    /// <summary>
    /// Returns exchange rates for all currencies relative to <paramref name="baseCurrency"/>.
    /// </summary>
    /// <param name="baseCurrency">ISO 4217 currency code, e.g. "EUR".</param>
    /// <param name="cancellationToken">Propagated from the HTTP request.</param>
    Task<Dictionary<string, decimal>> GetRatesAsync(
        string baseCurrency,
        CancellationToken cancellationToken = default);
}
