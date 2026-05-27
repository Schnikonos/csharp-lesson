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

## Next Part

```bash
git checkout lesson/01-endpoint/c-advanced
# Topics: Polly retry + circuit breaker, response caching, timeout policy
```
