using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Lesson.Config;
using Lesson.Services;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;

namespace Lesson.Tests;

// =============================================================================
// UNIT TEST: ExchangeRateService
//
// WHAT WE ARE TESTING:
//   ExchangeRateService.GetRatesAsync() — the typed HttpClient service.
//
// THE CHALLENGE:
//   The service makes a real HTTP call. In unit tests we must NOT hit a real
//   network. We also want to control exactly what the "server" returns so our
//   assertions are deterministic.
//
// THE SOLUTION — Mock HttpMessageHandler:
//   HttpClient is just a thin wrapper around HttpMessageHandler, which has one
//   method: SendAsync(). We mock that method to return a fake HttpResponseMessage.
//
// Java parallel:
//   MockRestServiceServer (Spring Test)   →  mocked HttpMessageHandler
//   WireMock                              →  MockHttpMessageHandler (same concept)
//   Mockito.when(restTemplate.get...).thenReturn(...)  →  handlerMock.Protected().Setup(...)
//
// TEST TYPES IN THIS FILE:
//   - Pure unit tests (no running server, no DI container, no EF Core)
//   - Uses Moq for mocking, FluentAssertions for readable assertions
// =============================================================================

public class ExchangeRateServiceTests
{
    // -------------------------------------------------------------------------
    // Helper: creates an ExchangeRateService with a mocked HttpMessageHandler
    // that returns the given JSON body and HTTP status code.
    //
    // C# NOTE: "out" parameter — the caller receives the mock so it can
    // verify calls were made on it after the act step.
    // -------------------------------------------------------------------------
    private static ExchangeRateService CreateService(
        string jsonBody,
        HttpStatusCode statusCode,
        out Mock<HttpMessageHandler> handlerMock)
    {
        handlerMock = new Mock<HttpMessageHandler>();

        // Protected() is a Moq extension that lets you mock protected/internal
        // methods — SendAsync is protected on HttpMessageHandler.
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = statusCode,
                Content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
            });

        var httpClient = new HttpClient(handlerMock.Object);

        var options = Options.Create(new ExchangeRateOptions
        {
            BaseUrl = "https://fake-api.test/v6/latest",
            TimeoutSeconds = 5
        });

        return new ExchangeRateService(httpClient, options);
    }

    // =========================================================================
    // TEST 1 — Happy path: API returns valid rates
    // =========================================================================
    [Fact]
    public async Task GetRatesAsync_WhenApiReturnsRates_ReturnsDictionary()
    {
        // ----- Arrange -------------------------------------------------------
        var fakeJson = JsonSerializer.Serialize(new
        {
            result = "success",
            rates = new Dictionary<string, decimal>
            {
                ["USD"] = 1.08m,
                ["GBP"] = 0.85m
            }
        });

        var service = CreateService(fakeJson, HttpStatusCode.OK, out _);

        // ----- Act -----------------------------------------------------------
        var rates = await service.GetRatesAsync("EUR");

        // ----- Assert --------------------------------------------------------
        // FluentAssertions: more readable than Assert.Equal(expected, actual)
        // Java parallel: AssertJ  assertThat(rates).containsKey("USD")
        rates.Should().ContainKey("USD").WhoseValue.Should().Be(1.08m);
        rates.Should().ContainKey("GBP").WhoseValue.Should().Be(0.85m);
    }

    // =========================================================================
    // TEST 2 — API returns HTTP error → HttpRequestException is thrown
    // =========================================================================
    [Fact]
    public async Task GetRatesAsync_WhenApiReturns500_ThrowsHttpRequestException()
    {
        // ----- Arrange -------------------------------------------------------
        var service = CreateService("{}", HttpStatusCode.InternalServerError, out _);

        // ----- Act & Assert --------------------------------------------------
        // C# NOTE: Assert.ThrowsAsync is the xUnit way to assert exceptions
        // from async methods.
        // Java parallel: assertThrows(HttpClientErrorException.class, () -> ...)
        await Assert.ThrowsAsync<HttpRequestException>(
            () => service.GetRatesAsync("EUR"));
    }

    // =========================================================================
    // TEST 3 — Verify the correct URL was called
    // =========================================================================
    [Fact]
    public async Task GetRatesAsync_CallsCorrectUrl()
    {
        // ----- Arrange -------------------------------------------------------
        var fakeJson = JsonSerializer.Serialize(new { rates = new Dictionary<string, decimal>() });
        var service = CreateService(fakeJson, HttpStatusCode.OK, out var handlerMock);

        // ----- Act -----------------------------------------------------------
        await service.GetRatesAsync("EUR");

        // ----- Assert --------------------------------------------------------
        // Verify that SendAsync was called exactly once with a GET request
        // whose URL ends with "EUR".
        handlerMock
            .Protected()
            .Verify(
                "SendAsync",
                Times.Once(),
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.Method == HttpMethod.Get &&
                    req.RequestUri!.ToString().EndsWith("EUR")),
                ItExpr.IsAny<CancellationToken>());
    }

    // =========================================================================
    // TEST 4 — CancellationToken is propagated to the HTTP call
    // =========================================================================
    [Fact]
    public async Task GetRatesAsync_WhenCancelled_ThrowsOperationCanceledException()
    {
        // ----- Arrange -------------------------------------------------------
        // Simulate a slow network: the handler delays, but the token is cancelled
        // before the response arrives.
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>(async (_, ct) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(5), ct); // will be cancelled
                return new HttpResponseMessage(HttpStatusCode.OK);
            });

        var httpClient = new HttpClient(handlerMock.Object);
        var options = Options.Create(new ExchangeRateOptions
        {
            BaseUrl = "https://fake-api.test/v6/latest",
            TimeoutSeconds = 30
        });
        var service = new ExchangeRateService(httpClient, options);

        using var cts = new CancellationTokenSource();

        // ----- Act -----------------------------------------------------------
        // Cancel immediately after starting the call
        var task = service.GetRatesAsync("EUR", cts.Token);
        await cts.CancelAsync();

        // ----- Assert --------------------------------------------------------
        // C# NOTE: OperationCanceledException is the base; TaskCanceledException
        // (which inherits it) is what HttpClient throws when cancelled.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }
}
