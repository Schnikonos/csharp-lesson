# Lesson 01-B — Typed HttpClient + async/await + IOptions (Intermediate)

**Branch:** `lesson/01-endpoint/b-intermediate`
**Previous:** `lesson/01-endpoint/a-basic` | **Next:** `lesson/01-endpoint/c-advanced`

## What Was Added

- `Lesson/Config/ExchangeRateOptions.cs` — strongly-typed config POCO (`IOptions<T>`)
- `Lesson/Services/IExchangeRateService.cs` — async interface with `CancellationToken`
- `Lesson/Services/ExchangeRateService.cs` — typed `HttpClient` implementation
- `Lesson/Controllers/ExchangeRateController.cs` — `GET /exchangerate/{base}`
- `Lesson/appsettings.json` — `ExchangeRate` config section added
- `Lesson.Tests/ExchangeRateServiceTests.cs` — 4 unit tests (mock `HttpMessageHandler`)
- `Lesson.Tests/AccountControllerTests.cs` — 5 integration tests (`WebApplicationFactory`)

## New Endpoint

`GET /exchangerate/{currency}` — returns all exchange rates relative to the given ISO currency code.

## Key Concepts

### 1. Why NOT `new HttpClient()`
`new HttpClient()` holds a socket that is NOT released on Dispose — causes socket exhaustion under load.
`IHttpClientFactory` manages a pool of `HttpMessageHandler` instances with correct lifetime and DNS TTL handling.
Java parallel: `new RestTemplate()` per request (bad) vs a `@Bean`-scoped instance (good).

### 2. async / await
`await` suspends the method and returns the thread to the pool while I/O is in flight.
The compiler generates a state machine — no thread is blocked.
Java parallel: `CompletableFuture.supplyAsync(...)` + `.thenApply(...)`.

### 3. CancellationToken propagation
ASP.NET Core automatically binds a `CancellationToken` controller parameter to the HTTP request lifecycle.
Pass it all the way down to `HttpClient.GetAsync()` — if the client disconnects, the outgoing call is aborted.

### 4. IOptions<T>
Binds a named `appsettings.json` section to a strongly-typed POCO at startup.
Java parallel: `@ConfigurationProperties(prefix = "exchange-rate")`.

### 5. Testing a typed HttpClient
Mock `HttpMessageHandler.SendAsync` (protected) with `Moq.Protected()` — no real network call.
Java parallel: `MockRestServiceServer` / WireMock.

### 6. WebApplicationFactory
Boots the real app in-memory. Override any DI service with a mock per test.
Java parallel: `@SpringBootTest(webEnvironment = RANDOM_PORT)` + `@MockBean`.

## Running the Tests

```bash
dotnet test Lesson.Tests/Lesson.Tests.csproj
# 9 tests: 4 unit (ExchangeRateService) + 5 integration (AccountController)
```

## Exercises

1. Add `GET /exchangerate/{base}/{target}` — return a single rate, `404` if not found.
2. Catch `TaskCanceledException` in the controller and return `504 Gateway Timeout`.
3. Write a test for the `502` path — mock handler returns `500`, assert controller returns `502`.
4. Try `IOptionsSnapshot<T>` — observe hot-reload behaviour vs `IOptions<T>`.
5. Register a second named client: `AddHttpClient("audit-log")` and inject via `IHttpClientFactory.CreateClient("audit-log")`.

## Next Part

```bash
git checkout lesson/01-endpoint/c-advanced
# Topics: Polly retry + circuit breaker, response caching, timeout policy
```
