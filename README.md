# Lesson 01 — Endpoint + Service (Intermediate)

**Level:** Intermediate

```bash
dotnet test Lesson.Tests/Lesson.Tests.csproj
# 9 tests: 4 unit (ExchangeRateService) + 5 integration (AccountController)
```

Typed HttpClient calling an external rate API, IHttpClientFactory, async/await, CancellationToken, IOptions<T> for base URL.

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
