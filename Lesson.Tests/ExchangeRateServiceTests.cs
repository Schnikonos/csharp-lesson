using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Lesson.Services;
using Moq;
using Moq.Protected;

namespace Lesson.Tests;

// =============================================================================
// UNIT TEST: ExchangeRateService
//
// CHANGED IN LESSON 01-C:
//   The service constructor no longer accepts IOptions<ExchangeRateOptions>.
//   BaseAddress and Timeout are now set in the DI builder (Program.cs).
//   In tests we set them directly on the HttpClient — same effect.
//
// Java parallel:
//   MockRestServiceServer / WireMock  →  mocked HttpMessageHandler
// =============================================================================

public class ExchangeRateServiceTests
{
    private static ExchangeRateService CreateService(
        string jsonBody,
        HttpStatusCode statusCode,
        out Mock<HttpMessageHandler> handlerMock)
    {
        handlerMock = new Mock<HttpMessageHandler>();
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = statusCode,
                Content    = new StringContent(jsonBody, Encoding.UTF8, "application/json")
            });

        var httpClient = new HttpClient(handlerMock.Object)
        {
            BaseAddress = new Uri("https://fake-api.test/v6/latest/"),
            Timeout     = TimeSpan.FromSeconds(5)
        };

        return new ExchangeRateService(httpClient);
    }

    // =========================================================================
    // TEST 1 — Happy path: API returns valid rates
    // =========================================================================
    [Fact]
    public async Task GetRatesAsync_WhenApiReturnsRates_ReturnsDictionary()
    {
        var fakeJson = JsonSerializer.Serialize(new
        {
            result = "success",
            rates  = new Dictionary<string, decimal> { ["USD"] = 1.08m, ["GBP"] = 0.85m }
        });
        var service = CreateService(fakeJson, HttpStatusCode.OK, out _);

        var rates = await service.GetRatesAsync("EUR");

        rates.Should().ContainKey("USD").WhoseValue.Should().Be(1.08m);
        rates.Should().ContainKey("GBP").WhoseValue.Should().Be(0.85m);
    }

    // =========================================================================
    // TEST 2 — API returns HTTP error → HttpRequestException is thrown
    // =========================================================================
    [Fact]
    public async Task GetRatesAsync_WhenApiReturns500_ThrowsHttpRequestException()
    {
        var service = CreateService("{}", HttpStatusCode.InternalServerError, out _);
        await Assert.ThrowsAsync<HttpRequestException>(() => service.GetRatesAsync("EUR"));
    }

    // =========================================================================
    // TEST 3 — Verify the correct URL was called
    // =========================================================================
    [Fact]
    public async Task GetRatesAsync_CallsCorrectUrl()
    {
        var fakeJson = JsonSerializer.Serialize(new { rates = new Dictionary<string, decimal>() });
        var service  = CreateService(fakeJson, HttpStatusCode.OK, out var handlerMock);

        await service.GetRatesAsync("EUR");

        handlerMock.Protected().Verify(
            "SendAsync", Times.Once(),
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
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>(async (_, ct) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
                return new HttpResponseMessage(HttpStatusCode.OK);
            });

        var httpClient = new HttpClient(handlerMock.Object)
        {
            BaseAddress = new Uri("https://fake-api.test/v6/latest/"),
            Timeout     = TimeSpan.FromSeconds(30)
        };
        var service = new ExchangeRateService(httpClient);

        using var cts = new CancellationTokenSource();
        var task = service.GetRatesAsync("EUR", cts.Token);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }
}
