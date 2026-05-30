using System.Net;
using System.Text;
using FluentAssertions;
using Lesson.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Polly;
using Polly.Retry;

namespace Lesson.Tests;

// =============================================================================
// LESSON 01-C TESTS: Resilience Pipeline + Output Caching
//
// Three scenarios:
//  1. Output cache — second identical request served from cache (service not
//     called again).
//  2. Output cache — different currencies are cached independently (each
//     triggers one service call).
//  3. Polly retry  — handler fails twice, succeeds on the third attempt;
//     the final result is the success response.
//
// WHY INTEGRATION TESTS FOR CACHING?
//   The output cache is wired in Program.cs (the DI root + middleware pipeline).
//   WebApplicationFactory spins up the full real pipeline without a TCP server,
//   so we can verify the cache actually intercepts the second request.
//
// Java parallel:
//   @SpringBootTest + MockMvc / WebTestClient with Mockito stubs.
// =============================================================================

public class ResilienceTests : IClassFixture<WebApplicationFactory<Program>>
{
    // =========================================================================
    // TEST 1 — Second identical request served from output cache
    //
    // The mock service is registered as a singleton replacement.
    // After two GET /exchangerate/EUR calls the service should have been
    // called exactly once — the second response came from the 60-second cache.
    // =========================================================================
    [Fact]
    public async Task OutputCache_SecondRequest_ServesFromCacheWithoutCallingService()
    {
        // ----- Arrange -------------------------------------------------------
        var mockService = new Mock<IExchangeRateService>();
        mockService
            .Setup(s => s.GetRatesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, decimal> { ["USD"] = 1.08m });

        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(host =>
                host.ConfigureServices(services =>
                    services.AddSingleton(mockService.Object)));

        var client = factory.CreateClient();

        // ----- Act -----------------------------------------------------------
        var r1 = await client.GetAsync("/exchangerate/EUR");
        var r2 = await client.GetAsync("/exchangerate/EUR");

        // ----- Assert --------------------------------------------------------
        r1.StatusCode.Should().Be(HttpStatusCode.OK);
        r2.StatusCode.Should().Be(HttpStatusCode.OK);

        // The service must have been called only once — the second response
        // was served directly from the output cache.
        mockService.Verify(
            s => s.GetRatesAsync("EUR", It.IsAny<CancellationToken>()),
            Times.Once,
            "second request should be served from output cache, not call the service again");
    }

    // =========================================================================
    // TEST 2 — Different currencies are cached independently
    //
    // /exchangerate/EUR and /exchangerate/USD are different URL paths →
    // different cache keys → both must call the service once.
    // =========================================================================
    [Fact]
    public async Task OutputCache_DifferentCurrencies_CachedIndependently()
    {
        // ----- Arrange -------------------------------------------------------
        var mockService = new Mock<IExchangeRateService>();
        mockService
            .Setup(s => s.GetRatesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, decimal> { ["USD"] = 1.08m });

        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(host =>
                host.ConfigureServices(services =>
                    services.AddSingleton(mockService.Object)));

        var client = factory.CreateClient();

        // ----- Act -----------------------------------------------------------
        await client.GetAsync("/exchangerate/EUR");
        await client.GetAsync("/exchangerate/USD");

        // ----- Assert --------------------------------------------------------
        mockService.Verify(s => s.GetRatesAsync("EUR", It.IsAny<CancellationToken>()), Times.Once);
        mockService.Verify(s => s.GetRatesAsync("USD", It.IsAny<CancellationToken>()), Times.Once);
    }

    // =========================================================================
    // TEST 3 — Polly retry: transient failure retried, succeeds on 3rd attempt
    //
    // We build a minimal Polly pipeline directly (no full DI stack) to verify
    // the retry strategy logic in isolation. The handler returns 503 twice then
    // 200 — the pipeline should retry and return the success response.
    //
    // Java parallel:
    //   RetryRegistry.of(RetryConfig.custom().maxAttempts(3).build())
    //   Retry.decorateSupplier(retry, service::call)
    // =========================================================================
    [Fact]
    public async Task PollyRetry_TransientFailure_RetriesAndSucceeds()
    {
        // ----- Arrange -------------------------------------------------------
        int callCount = 0;
        const string successJson = """{"rates":{"USD":1.08}}""";

        var handler = new DelegatingHandlerStub(request =>
        {
            callCount++;
            if (callCount < 3)
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(successJson, Encoding.UTF8, "application/json")
            };
        });

        // Build a Polly v8 retry pipeline that retries on 5xx responses.
        var pipeline = new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
            {
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .HandleResult(r => (int)r.StatusCode >= 500),
                MaxRetryAttempts = 3,
                Delay            = TimeSpan.Zero   // no delay in tests
            })
            .Build();

        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://fake-api.test/v6/latest/")
        };

        // ----- Act -----------------------------------------------------------
        var finalResponse = await pipeline.ExecuteAsync(
            async ct => await httpClient.GetAsync("EUR", ct));

        // ----- Assert --------------------------------------------------------
        finalResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        callCount.Should().Be(3,
            "two 503 failures followed by one 200 success = 3 total handler calls");
    }

    // ─── Helper ──────────────────────────────────────────────────────────────
    // A lightweight DelegatingHandler backed by a sync factory function.
    // Avoids the Moq.Protected() ceremony when the handler logic is simple.
    private sealed class DelegatingHandlerStub : DelegatingHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _factory;

        public DelegatingHandlerStub(Func<HttpRequestMessage, HttpResponseMessage> factory)
            => _factory = factory;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_factory(request));
    }
}
