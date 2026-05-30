# Lesson 01 — Endpoint + Service (Advanced)

**Level:** Advanced

```bash
dotnet test Lesson.Tests/Lesson.Tests.csproj
# 9 tests: 4 unit (ExchangeRateService) + 5 integration (AccountController)
```

Polly resilience (retry + circuit breaker), response caching, timeout policy, HttpClient best practices.

1. Add `GET /exchangerate/{base}/{target}` — return a single rate, `404` if not found.
2. Catch `TaskCanceledException` in the controller and return `504 Gateway Timeout`.
3. Write a test for the `502` path — mock handler returns `500`, assert controller returns `502`.
4. Try `IOptionsSnapshot<T>` — observe hot-reload behaviour vs `IOptions<T>`.
5. Register a second named client: `AddHttpClient("audit-log")` and inject via `IHttpClientFactory.CreateClient("audit-log")`.

## What you will learn

| Topic | C# / ASP.NET Core | Java / Spring Boot parallel |
|---|---|---|
| HTTP resilience pipeline | `Microsoft.Extensions.Http.Resilience` (Polly v8) | Resilience4j |
| Retry with exponential back-off + jitter | `options.Retry.*` | `RetryConfig.custom()` |
| Circuit breaker | `options.CircuitBreaker.*` | `CircuitBreakerConfig.custom()` |
| Per-attempt & total timeout | `AttemptTimeout` + `TotalRequestTimeout` | `TimeLimiterConfig` |
| Response caching | `AddOutputCache` + `[OutputCache]` | `@Cacheable` + `CacheManager` |
| Config in DI root, not in service | Client configured in `Program.cs` | `@Bean WebClient` in `@Configuration` |

## Key changes from 01-B

### HttpClient configuration moved to `Program.cs`
In 01-B the constructor set `BaseAddress`/`Timeout`. Now those live in the
`AddHttpClient` fluent builder — the service class has a single dependency: `HttpClient`.

### Standard Resilience Pipeline
`AddStandardResilienceHandler()` stacks five strategies:


- **Retry** — exponential back-off with jitter, 3 attempts on transient errors (5xx, 408, 429)
- **Circuit Breaker** — opens at 50% failure rate (min 5 requests / 30 s window), stays open 15 s
- **Timeouts** — 5 s per attempt, 30 s total

### Output Caching
`[OutputCache(PolicyName = "ExchangeRates")]` caches the full response for 60 seconds.
The controller is **not called** on a cache hit. Cache key = URL path, so each currency is cached separately.


## Exercises

1. Add a `/exchangerate/{base}/live` endpoint with `[OutputCache(Duration = 0)]` that always bypasses the cache.
2. Add an `OnRetry` callback that logs the attempt number — verify the log in a test.
3. Lower `MinimumThroughput` to 2 and write a test that opens the circuit and asserts 502 without hitting the service.

## Tests

| File | Count | Covers |
|---|---|---|
| `ExchangeRateServiceTests.cs` | 4 | Updated service (new constructor) |
| `ResilienceTests.cs` | 3 | Cache hit (×1 service call), independent keys, Polly retry |


## Next Part

```bash
git checkout lesson/01-endpoint/c-advanced
# Topics: Polly retry + circuit breaker, response caching, timeout policy
```

