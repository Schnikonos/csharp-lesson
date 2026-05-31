# Lesson 15-C — OpenTelemetry: Traces, Metrics, ActivitySource, and Health Checks

> **Branch:** `lesson/15-logging/c-advanced`
> **Prerequisites:** Lesson 15-B (Serilog)

---

## What you will learn

| Topic | .NET OpenTelemetry | Java (Micrometer / OTel SDK) |
|---|---|---|
| SDK wiring | `AddOpenTelemetry()` in `Program.cs` | `io.opentelemetry:opentelemetry-spring-boot-starter` |
| Custom spans | `ActivitySource` + `StartActivity(name)` | `Tracer.spanBuilder(name).startSpan()` |
| Span attributes | `activity.SetTag(key, value)` | `span.setAttribute(key, value)` |
| Span events | `activity.AddEvent(new ActivityEvent(...))` | `span.addEvent(name)` |
| Mark span failed | `activity.SetStatus(Error, desc)` | `span.setStatus(StatusCode.ERROR, desc)` |
| HTTP instrumentation | `AddAspNetCoreInstrumentation()` | `OpenTelemetryMeterRegistry` / micrometer-tracing |
| DB instrumentation | `AddEntityFrameworkCoreInstrumentation()` | `opentelemetry-jdbc` |
| Metrics | `WithMetrics(...)` + console exporter | `MeterRegistry` + `ConsoleMeterRegistry` |
| Exporter | Console (dev) / OTLP (Jaeger/Zipkin in prod) | `JaegerSpanExporter` / OTLP |
| Health checks | `AddHealthChecks().AddDbContextCheck<T>()` | Spring Boot Actuator `HealthIndicator` |
| Health endpoint | `app.MapHealthChecks("/health")` | `/actuator/health` |

---

## 1. Wiring OpenTelemetry

```csharp
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("BankingApi"))
    .WithTracing(tracing => tracing
        .AddSource("BankingApi")                    // your custom ActivitySource name
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddEntityFrameworkCoreInstrumentation()
        .AddConsoleExporter())                       // swap to .AddOtlpExporter() for Jaeger
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddConsoleExporter());
```

**Java parallel:**
```java
@Bean OpenTelemetrySdkAutoConfiguration ...  // or opentelemetry-spring-boot-starter
```

---

## 2. Custom spans with ActivitySource

```csharp
// Declare once per class (static field)
private static readonly ActivitySource Source = new("BankingApi");

// Inside a method:
using var activity = Source.StartActivity("FetchAccount");
activity?.SetTag("account.id", id);
activity?.AddEvent(new ActivityEvent("db.query.started"));

// Mark as failed
activity?.SetStatus(ActivityStatusCode.Error, "account not found");
```

The span automatically becomes a **child** of the current HTTP request span. In Jaeger you see:
```
[HTTP GET /otel-demo/accounts/1]
  ??? [FetchAccount]  (account.id=1)
        ??? [main]    (EF Core SQLite query)
```

**Java parallel:**
```java
Span span = tracer.spanBuilder("FetchAccount").startSpan();
try (Scope scope = span.makeCurrent()) {
    span.setAttribute("account.id", id);
    // ...
} finally {
    span.end();
}
```

---

## 3. Multi-span traces

```csharp
using var transferActivity = Source.StartActivity("Transfer");
using (var debitActivity   = Source.StartActivity("DebitSource"))  { ... }
using (var creditActivity  = Source.StartActivity("CreditDestination")) { ... }
```

This produces a tree:
```
[Transfer]
  ??? [DebitSource]   ? EF Core child span
  ??? [CreditDestination] ? EF Core child span
```

---

## 4. Health checks

```csharp
// Registration
builder.Services.AddHealthChecks()
    .AddDbContextCheck<BankingDbContext>("database");

// Endpoint
app.MapHealthChecks("/health");
```

`GET /health` returns:
```json
{ "status": "Healthy", "results": { "database": { "status": "Healthy" } } }
```

**Java parallel:**
```java
@Component
class DatabaseHealthIndicator implements HealthIndicator {
    public Health health() { ... }
}
// ? GET /actuator/health
```

---

## Project Structure (new / changed files)

```
Lesson/
  Controllers/
    OtelDemoController.cs  NEW  GET /otel-demo/accounts/{id},
                                POST /otel-demo/transfer (multi-span trace)
  Program.cs               MOD  AddOpenTelemetry(), AddHealthChecks(),
                                MapHealthChecks("/health"), AddSource("BankingApi")
Lesson.Tests/
  OtelDemoTests.cs         NEW  5 integration tests (including /health check)
```

---

## Tests

```bash
dotnet test --filter "FullyQualifiedName~OtelDemoTests"
# 5 tests — all pass
# Console output shows real spans with TraceId / SpanId / Tags
```

---

## Exercises

1. Run Jaeger locally (`docker run -d -p 16686:16686 -p 4317:4317 jaegertracing/all-in-one`) and swap `.AddConsoleExporter()` for `.AddOtlpExporter()` — observe the multi-span transfer trace in the UI.
2. Add a `Meter("BankingApi")` static field to `OtelDemoController` and create a `Counter` that increments on each successful transfer — verify it appears in the console metric output.
3. Add a second health check for a dummy external dependency using `AddCheck("exchange-rate-api", () => HealthCheckResult.Healthy("API reachable"))` and verify `/health` shows both.


> **Branch:** `lesson/15-logging/b-intermediate`
> **Prerequisites:** Lesson 15-A (ILogger&lt;T&gt; basics)

---

## What you will learn

| Topic | Serilog (.NET) | Java (Logback / Log4j2) |
|---|---|---|
| Replace default logging | `builder.Host.UseSerilog()` | `logback.xml` / `log4j2.xml` |
| Console sink | `WriteTo.Console()` | `ConsoleAppender` |
| File sink (rolling) | `WriteTo.File(rollingInterval: Day)` | `RollingFileAppender` |
| Structured properties | `{CorrelationId}` stored in JSON | MDC key stored in pattern |
| Ambient context | `LogContext.PushProperty(key, val)` | `MDC.put(key, val)` |
| Per-request logging | `UseSerilogRequestLogging()` | `logback-access` / Actuator access log |
| HTTP context in services | `IHttpContextAccessor` | `@RequestScope` / `HttpServletRequest` |

---

## 1. Wiring Serilog

```csharp
// Program.cs
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)  // reads "Serilog" section
    .Enrich.FromLogContext()                          // picks up PushProperty
    .CreateLogger();

builder.Host.UseSerilog();  // replaces Microsoft.Extensions.Logging
```

**Java parallel:**
```xml
<!-- logback.xml — replaces java.util.logging / commons-logging -->
<configuration>
  <appender name="CONSOLE" class="ch.qos.logback.core.ConsoleAppender">...</appender>
  <root level="INFO"><appender-ref ref="CONSOLE"/></root>
</configuration>
```

---

## 2. Correlation ID propagation

```csharp
// Push a property onto the current async context —
// all log entries emitted below this line carry CorrelationId.
using (LogContext.PushProperty("CorrelationId", correlationId))
{
    logger.LogInformation("Fetching account {AccountId}", id);
    // ? {Timestamp: ..., CorrelationId: "abc123", AccountId: 42, ...}
}
```

**Java parallel (MDC):**
```java
MDC.put("correlationId", correlationId);
try {
    log.info("Fetching account {}", id);
} finally {
    MDC.clear();
}
```

---

## 3. Serilog configuration (appsettings.json)

```json
"Serilog": {
  "MinimumLevel": {
    "Default": "Information",
    "Override": {
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.EntityFrameworkCore.Database.Command": "Warning"
    }
  },
  "WriteTo": [
    { "Name": "Console" },
    { "Name": "File", "Args": { "path": "logs/banking-.log", "rollingInterval": "Day" } }
  ],
  "Enrich": [ "FromLogContext" ]
}
```

---

## 4. UseSerilogRequestLogging

```csharp
app.UseSerilogRequestLogging();
```

Replaces the verbose default host request logs with one structured line per request:
```
[09:31:20 INF] HTTP GET /accounts/1 responded 200 in 3.4ms
```

---

## Project Structure (new / changed files)

```
Lesson/
  Controllers/
    SerilogDemoController.cs   NEW  GET /serilog-demo/accounts/{id},
                                    GET /serilog-demo/enrich
  Program.cs                   MOD  UseSerilog(), AddHttpContextAccessor(),
                                    UseSerilogRequestLogging()
  appsettings.json              MOD  "Serilog" configuration section added
Lesson.Tests/
  SerilogDemoTests.cs          NEW  4 integration tests
```

---

## Tests

```bash
dotnet test --filter "FullyQualifiedName~SerilogDemoTests"
# 4 tests — all pass
```

---

## Exercises

1. Add a `WriteTo.Seq("http://localhost:5341")` sink and run Seq locally (`docker run --rm -e ACCEPT_EULAS=Y -p 5341:80 datalust/seq`) — query `CorrelationId = "test-cid-123"` in the UI.
2. Create an enricher that automatically reads `X-Correlation-ID` from the HTTP context and pushes it via `LogContext` for every request, so individual controllers don't have to do it themselves.
3. Override the `UseSerilogRequestLogging` message template to include `{UserName}` from the JWT claims.


> **Branch:** `lesson/15-logging/a-basic`
> **Prerequisites:** Lesson 14-C (caching)

---

## What you will learn

| Topic | C# ILogger&lt;T&gt; | Java SLF4J / Log4j2 |
|---|---|---|
| Get logger | Injected via DI constructor | `LoggerFactory.getLogger(getClass())` |
| Log levels | `Trace Debug Info Warn Error Critical` | `TRACE DEBUG INFO WARN ERROR FATAL` |
| Message template | `LogInformation("Account {AccountId}", id)` | `logger.info("Account {}", id)` |
| Structured property | `{AccountId}` stored as named field | MDC / `%X{accountId}` in pattern |
| Log scope | `logger.BeginScope(dictionary)` | `MDC.put(key, value)` / `ThreadContext` |
| Minimum level config | `"Logging:LogLevel:Default": "Information"` | `<Logger level="INFO">` / `logging.level.root=INFO` |

---

## 1. Message templates — why not string interpolation?

```csharp
// ? Correct — {AccountId} is a named property
logger.LogInformation("Fetching account {AccountId}", id);

// ? Wrong — collapses structure into a plain string
logger.LogInformation($"Fetching account {id}");
```

With a structured sink (Seq, Elastic, Application Insights) the named property is stored separately and becomes queryable: `AccountId == 42`. String interpolation destroys this.

**Java parallel:**
```java
// SLF4J uses {} placeholders — NOT string format
logger.info("Fetching account {}", id);
// MDC for ambient context
MDC.put("accountId", String.valueOf(id));
```

---

## 2. Log levels

| Level | When to use |
|---|---|
| `Trace` | Extremely detailed — byte-level, loop-level (usually disabled) |
| `Debug` | Developer diagnostics — entry/exit of methods |
| `Information` | Normal flow — request started, record created |
| `Warning` | Recoverable anomaly — not found, retry triggered |
| `Error` | Operation failed — exception caught, DB write failed |
| `Critical` | System-wide failure — app cannot continue, data corruption |

Configure minimum level in `appsettings.json`:
```json
"Logging": {
  "LogLevel": {
    "Default": "Information",
    "Microsoft.AspNetCore": "Warning",
    "Microsoft.EntityFrameworkCore.Database.Command": "Warning"
  }
}
```

---

## 3. Log scopes — ambient context

```csharp
using (logger.BeginScope(new Dictionary<string, object>
{
    ["TransferId"] = Guid.NewGuid(),
    ["From"]       = request.From,
    ["To"]         = request.To,
}))
{
    logger.LogInformation("Starting transfer of {Amount}", request.Amount);
    // All log entries within this block include TransferId, From, To
}
```

**Java parallel:**
```java
MDC.put("transferId", UUID.randomUUID().toString());
try {
    log.info("Starting transfer of {}", amount);
} finally {
    MDC.clear();
}
```

---

## Project Structure (new / changed files)

```
Lesson/
  Controllers/
    LoggingDemoController.cs  NEW  GET /logging-demo/accounts/{id},
                                   POST /logging-demo/transfer (log scope),
                                   GET /logging-demo/levels
Lesson.Tests/
  LoggingDemoTests.cs         NEW  5 integration tests
```

---

## Tests

```bash
dotnet test --filter "FullyQualifiedName~LoggingDemoTests"
# 5 tests — all pass
```

---

## Exercises

1. Change `appsettings.Development.json` to set `"Lesson.Controllers": "Warning"` and run the app — verify the `Information` messages from `LoggingDemoController` are suppressed.
2. Add an `EventId` to a log call: `logger.LogInformation(new EventId(1001, "TransferStarted"), "Transfer {Amount}", amount)` — this lets you filter by event in structured sinks.
3. Replace `BeginScope(dictionary)` with `BeginScope("Processing transfer {TransferId}", id)` — note how the format differs from the dictionary approach and when each is preferred.


> **Branch:** `lesson/14-caching/c-advanced`
> **Prerequisites:** Lesson 14-B (IDistributedCache)

---

## What you will learn

| Topic | C# .NET 7+ | Java Spring |
|---|---|---|
| Client-hint headers | `[ResponseCache(Duration = 30)]` ? `Cache-Control: max-age=30` | `@RequestMapping` + `@CacheControl` / HttpServletResponse headers |
| Server-side response cache | `[OutputCache(Duration = 60)]` | `@Cacheable` on controller method |
| Vary by query param | `[OutputCache(VaryByQueryKeys = ["type"])]` | `@Cacheable(key = "#type")` |
| Named policy | `[OutputCache(PolicyName = "Lock")]` | `@Cacheable(sync = true)` |
| Register pipeline | `AddOutputCache()`, `UseOutputCache()` | Spring Cache auto-config |
| Anti-stampede | `opts.AddPolicy("Lock", pb => pb.SetLocking(true))` | `@Cacheable(sync = true)` |

---

## 1. [ResponseCache] — tells clients and CDNs to cache

```csharp
[HttpGet("accounts/{id}")]
[ResponseCache(Duration = 30, Location = ResponseCacheLocation.Any)]
public IActionResult Get(int id) { ... }
// ? HTTP response includes: Cache-Control: public, max-age=30
```

No server memory is used. The browser (or CDN) caches the response for 30 seconds.  
The server still receives the request if the client's cache is cold.

**Java parallel:** Set `Cache-Control` header manually in the response, or use Spring's `@RequestMapping` + `WebContentInterceptor`.

---

## 2. [OutputCache] — server stores the full response

```csharp
[HttpGet("accounts/{id}")]
[OutputCache(Duration = 60)]
public async Task<IActionResult> Get(int id) { ... }
// First request ? executes controller, stores response in server memory
// Subsequent requests within 60 s ? served from server cache, controller NOT called
```

**Java parallel:**
```java
@Cacheable(value = "output", key = "#id")
@GetMapping("/accounts/{id}")
public ResponseEntity<Account> get(@PathVariable int id) { ... }
```

---

## 3. VaryByQueryKeys

Different query-string combinations produce **separate** cache entries:

```csharp
[OutputCache(Duration = 30, VaryByQueryKeys = ["type", "page"])]
public IActionResult List([FromQuery] string? type, [FromQuery] int page) { ... }
// /list?type=Savings&page=1  ? own cache entry
// /list?type=Checking&page=1 ? separate cache entry
```

---

## 4. Cache stampede prevention with the Lock policy

When a popular entry expires, hundreds of simultaneous requests can all hit the DB at once.  
The `Lock` policy serialises concurrent requests for the **same key** — only one hits the origin; others wait and then receive the cached response.

```csharp
// Register in Program.cs
builder.Services.AddOutputCache(opts =>
    opts.AddPolicy("Lock", pb => pb.SetLocking(true)));

// Apply on endpoint
[OutputCache(PolicyName = "Lock")]
public async Task<IActionResult> AntiStampede(int id) { ... }
```

**Java parallel:**
```java
@Cacheable(value = "safe", key = "#id", sync = true)
public Account getById(int id) { ... }
```

---

## 5. Pipeline order

```
UseExceptionHandler()
UseResponseCaching()   ? [ResponseCache] middleware — adds headers
UseOutputCache()       ? [OutputCache] middleware — serves/stores responses
UseAuthentication()
UseAuthorization()
MapControllers()
```

---

## Project Structure (new / changed files)

```
Lesson/
  Controllers/
    OutputCacheController.cs  NEW  GET /output-cache/headers/{id} ([ResponseCache]),
                                   GET /output-cache/server/{id} ([OutputCache]),
                                   GET /output-cache/server/list ([OutputCache] VaryByQuery),
                                   GET /output-cache/server/safe/{id} (Lock policy)
  Program.cs                       + AddOutputCache (Lock policy), AddResponseCaching,
                                   UseResponseCaching(), UseOutputCache()
Lesson.Tests/
  OutputCacheTests.cs         NEW  4 integration tests
```

---

## Tests

```bash
dotnet test --filter "FullyQualifiedName~OutputCacheTests"
# 4 tests — all pass
```

---

## Exercises

1. Add a `POST /output-cache/server/{id}/invalidate` endpoint that uses `IOutputCacheStore` to evict a specific entry by tag — you'll need to add `.Tag("account")` to the cache policy and call `store.EvictByTagAsync("account", ct)`.
2. Add a test that asserts `GET /output-cache/server/{id}` is **not** served from cache when the `Authorization` header differs (hint: add `VaryByHeader = ["Authorization"]`).
3. Compare benchmark results (via a simple `for` loop) between an uncached endpoint and a `[OutputCache]` endpoint — measure the median response time difference.


> **Branch:** `lesson/14-caching/b-intermediate`
> **Prerequisites:** Lesson 14-A (IMemoryCache)

---

## What you will learn

| Topic | C# .NET | Java Spring |
|---|---|---|
| Distributed cache interface | `IDistributedCache` | `CacheManager` / `RedisTemplate` |
| In-process dev backend | `AddDistributedMemoryCache()` | H2 / Caffeine (local test) |
| Redis production backend | `AddStackExchangeRedisCache(o => o.Configuration = "...")` | `spring.data.redis.host=...` |
| Get bytes | `await cache.GetAsync(key)` | `redisTemplate.opsForValue().get(key)` |
| Set bytes + options | `await cache.SetAsync(key, bytes, options)` | `redisTemplate.opsForValue().set(key, value, ttl, SECONDS)` |
| Remove | `await cache.RemoveAsync(key)` | `redisTemplate.delete(key)` |
| Serialize object | `JsonSerializer.Serialize(value)` ? `byte[]` | `objectMapper.writeValueAsBytes(value)` |

---

## 1. IDistributedCache vs IMemoryCache

| | `IMemoryCache` (14-A) | `IDistributedCache` (14-B) |
|---|---|---|
| Storage | In the process heap | External store (Redis, SQL, …) |
| Works across multiple servers | ? (single node) | ? (shared store) |
| Stores objects directly | ? | ? — must serialize to `byte[]` |
| No external dependency | ? | ? (needs Redis or DB) |
| Suitable for | Single-instance API, session, rate-limiting | Clustered APIs, session sharing, pub/sub |

---

## 2. Registration — swapping without changing code

```csharp
// Development — no Redis needed
if (builder.Environment.IsDevelopment())
    builder.Services.AddDistributedMemoryCache();

// Production — uncomment and set connection string
// builder.Services.AddStackExchangeRedisCache(o =>
//     o.Configuration = builder.Configuration.GetConnectionString("Redis"));
```

The controller only depends on `IDistributedCache` — swapping the provider is a **single line change** in `Program.cs`.

**Java parallel:**
```java
@Bean
CacheManager cacheManager(RedisConnectionFactory cf) {
    return RedisCacheManager.builder(cf).build();
}
// swap to CaffeineCacheManager for local dev
```

---

## 3. Serialization

`IDistributedCache` stores `byte[]`, not objects:

```csharp
// Write
await cache.SetAsync(
    $"account:{id}",
    Encoding.UTF8.GetBytes(JsonSerializer.Serialize(account)),
    new DistributedCacheEntryOptions
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),
        SlidingExpiration               = TimeSpan.FromMinutes(2),
    });

// Read
var bytes   = await cache.GetAsync($"account:{id}");
var account = JsonSerializer.Deserialize<BankAccount>(Encoding.UTF8.GetString(bytes!));
```

---

## 4. Running a real Redis (optional)

```bash
# Docker
docker run -p 6379:6379 redis:latest

# Then in appsettings.json
"ConnectionStrings": {
  "Redis": "localhost:6379"
}
# and uncomment AddStackExchangeRedisCache in Program.cs
```

---

## Project Structure (new / changed files)

```
Lesson/
  Controllers/
    DistributedCacheController.cs  NEW  GET /distributed-cache/accounts/{id},
                                        POST /distributed-cache/accounts,
                                        DELETE /distributed-cache/accounts/{id}/cache
  Program.cs                            + AddDistributedMemoryCache() (dev) / Redis comment
Lesson.Tests/
  DistributedCacheTests.cs         NEW  4 integration tests
```

---

## Tests

```bash
dotnet test --filter "FullyQualifiedName~DistributedCacheTests"
# 4 tests — all pass (using AddDistributedMemoryCache for test isolation)
```

---

## Exercises

1. Wrap the serialization helpers into a generic `ICacheService<T>` with `GetOrSetAsync`, `RemoveAsync` — this is the real-world pattern to avoid `byte[]` boilerplate in controllers.
2. Change the expiry to 2 seconds, wait 3 seconds in a test, and assert the cache entry is gone.
3. Start a Redis Docker container and switch `Program.cs` to `AddStackExchangeRedisCache` — run the tests against real Redis and observe the `StackExchange.Redis` connection log.


> **Branch:** `lesson/14-caching/a-basic`
> **Prerequisites:** Lesson 13-C (auth, DI patterns)

---

## What you will learn

| Topic | C# .NET | Java Spring |
|---|---|---|
| Enable caching | `AddMemoryCache()` | `@EnableCaching` |
| Cache-aside read | `cache.GetOrCreateAsync(key, factory)` | `@Cacheable(value = "...", key = "#id")` |
| Manual set | `cache.Set(key, value, options)` | `@CachePut` |
| Manual get | `cache.TryGetValue(key, out T value)` | `cacheManager.getCache(...).get(key)` |
| Hard expiry | `AbsoluteExpirationRelativeToNow` | `@Cacheable(... expire = 300)` |
| Sliding expiry | `SlidingExpiration` | Caffeine `expireAfterAccess` |
| Invalidate on write | `cache.Remove(key)` | `@CacheEvict(value = "...", key = "#id")` |
| Evict all | `cache.Remove(allKey)` | `@CacheEvict(value = "...", allEntries = true)` |

---

## 1. Registering IMemoryCache

```csharp
// Program.cs
builder.Services.AddMemoryCache();
// IMemoryCache is registered as Singleton — safe to inject into Scoped controllers
```

**Java parallel:**
```java
@SpringBootApplication
@EnableCaching       // activates Spring's proxy-based cache infrastructure
public class App { ... }
```

---

## 2. The cache-aside pattern

```csharp
// Try cache first; on miss, load from DB and store result
var account = await cache.GetOrCreateAsync(
    $"account:{id}",
    async entry =>
    {
        entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
        entry.SlidingExpiration               = TimeSpan.FromMinutes(2);
        return await uow.Accounts.GetByIdAsync(id);
    });
```

The factory lambda only runs on a **cache miss** — identical to `@Cacheable` in Spring.

**Java parallel:**
```java
@Cacheable(value = "accounts", key = "#id")
public Optional<BankAccount> getById(int id) {
    return repo.findById(id);
}
```

---

## 3. Absolute vs sliding expiry

| | Absolute | Sliding |
|---|---|---|
| Resets on access? | No — hard deadline | Yes — deadline resets each time |
| Use case | Data that must refresh on schedule | Session/user data that should expire only after inactivity |
| C# | `AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)` | `SlidingExpiration = TimeSpan.FromMinutes(2)` |
| Both at once? | Yes — sliding resets, but absolute cap overrides | |

---

## 4. Invalidation on write

```csharp
[HttpPost("accounts")]
public async Task<IActionResult> Create(CreateAccountRequest request)
{
    await uow.Accounts.AddAsync(newAccount);
    await uow.CommitAsync();               // persist BEFORE touching cache

    cache.Remove("accounts:all");          // evict stale list entry

    return CreatedAtAction(...);
}
```

**Java parallel:**
```java
@CacheEvict(value = "accounts", allEntries = true)
public BankAccount create(CreateAccountRequest request) { ... }
```

---

## Project Structure (new / changed files)

```
Lesson/
  Controllers/
    MemoryCacheController.cs  NEW  GET /cache-demo/accounts/{id},
                                   GET /cache-demo/accounts,
                                   POST /cache-demo/accounts,
                                   DELETE /cache-demo/accounts/{id}/cache
  Program.cs                        + AddMemoryCache()
Lesson.Tests/
  MemoryCacheTests.cs         NEW  4 integration tests
```

---

## Tests

```bash
dotnet test --filter "FullyQualifiedName~MemoryCacheTests"
# 4 tests — all pass
```

---

## Exercises

1. Add a `GET /cache-demo/accounts/{id}/cache-info` endpoint that returns whether the entry is currently in cache (hint: `cache.TryGetValue` returns `true`/`false`).
2. Change the sliding expiry to 5 seconds and write a test that asserts the value is evicted after 6 seconds.
3. Add `MemoryCacheEntryOptions.RegisterPostEvictionCallback` to log when and why an entry was evicted (`EvictionReason.Expired`, `EvictionReason.Removed`, etc.).


> **Branch:** `lesson/13-auth-jwt/c-advanced`
> **Prerequisites:** Lesson 13-B (role-based auth, `IAuthorizationHandler`)

---

## What you will learn

| Topic | C# (manual) | Java Spring OAuth2 |
|---|---|---|
| Refresh token issuance | `RandomNumberGenerator.Fill(bytes)` ? Base64 | Handled by Spring Authorization Server |
| Single-use refresh token | Consumed on use (`TryConsume` removes from store) | `TokenStore` / `JdbcTokenStore` |
| Token revocation | `POST /auth/token/revoke` ? removes from store | `/oauth2/revoke` endpoint |
| Store design | In-memory `Dictionary<string, (Username, Role, ExpiresAt)>` | `oauth2_access_token` DB table |
| Rotation | Each refresh issues a new access + refresh pair | Configurable with `TokenSettings` |

---

## 1. Why refresh tokens?

Access tokens are short-lived (e.g. 1 hour) to limit damage if stolen. Rather than forcing re-login every hour, a long-lived *refresh token* lets the client silently obtain a new access token.

```
Client                        Server
  ? POST /auth/token/login ???? validates credentials
  ???? { accessToken, refreshToken }
  ?                            ?  (1 hour later, access token expires)
  ? POST /auth/token/refresh ??? validates refresh token
  ???? { new accessToken, new refreshToken }
  ? POST /auth/token/revoke ???? removes refresh token from store
```

---

## 2. Generating a secure refresh token

```csharp
var bytes = new byte[32];
RandomNumberGenerator.Fill(bytes);      // cryptographically secure
return Convert.ToBase64String(bytes);   // 44-char opaque token
```

**Java parallel:**
```java
var bytes = new byte[32];
new SecureRandom().nextBytes(bytes);
return Base64.getUrlEncoder().encodeToString(bytes);
```

---

## 3. Single-use rotation

On refresh, the server removes the old token and issues a brand-new pair. If an attacker steals a refresh token and uses it first, the legitimate client's next refresh will fail — raising an alert.

```csharp
public bool TryConsume(string token, out string? username, out string? role)
{
    if (!_tokens.TryGetValue(token, out var entry) || entry.ExpiresAt < DateTime.UtcNow)
    { username = role = null; return false; }
    _tokens.Remove(token);   // single-use
    ...
}
```

---

## 4. Revocation

```csharp
[HttpPost("revoke")]
[Authorize]
public IActionResult Revoke([FromBody] RevokeRequest request)
{
    store.Revoke(request.RefreshToken);
    return NoContent();
}
```

In production, set a `Revoked = true` flag in DB rather than deleting (audit trail).

---

## Project Structure (new / changed files)

```
Lesson/
  Controllers/
    RefreshTokenController.cs  NEW  POST /auth/token/login (returns pair),
                                    POST /auth/token/refresh (rotate),
                                    POST /auth/token/revoke (invalidate),
                                    TokenStore, RefreshRequest, RevokeRequest,
                                    TokenPairResponse
  Program.cs                        + AddSingleton<TokenStore>
Lesson.Tests/
  RefreshTokenTests.cs         NEW  6 integration tests (login, refresh, single-use,
                                    revoke, unauthenticated revoke attempt)
```

---

## Tests

```bash
dotnet test --filter "FullyQualifiedName~RefreshTokenTests"
# 6 tests — all pass
```

---

## Exercises

1. Persist `TokenStore` to SQLite — add a `RefreshTokens` table with EF Core, replacing the in-memory dictionary.
2. Add an `AbsoluteExpiry` check: if the original login was more than 30 days ago, force re-login even if the refresh token is still technically valid.
3. Integrate a sliding-expiry: each successful refresh resets the 7-day window so active users never get logged out.


> **Branch:** `lesson/13-auth-jwt/b-intermediate`
> **Prerequisites:** Lesson 13-A (JWT bearer setup)

---

## What you will learn

| Topic | C# ASP.NET Core | Java Spring Security |
|---|---|---|
| Role guard on endpoint | `[Authorize(Roles = "Teller,Manager")]` | `@PreAuthorize("hasAnyRole('TELLER','MANAGER')")` |
| Role check in code | `User.IsInRole("Manager")` | `auth.getAuthorities().contains(...)` |
| Custom requirement | `IAuthorizationRequirement` + `AuthorizationHandler<TReq, TResource>` | `@Component` that implements `PermissionEvaluator` |
| Register policy | `options.AddPolicy("Name", p => p.Requirements.Add(...))` | `@PreAuthorize("@myBean.check(...)")` |
| Evaluate policy | `IAuthorizationService.AuthorizeAsync(User, resource, "Name")` | Handled by `@PreAuthorize` AOP |
| 401 vs 403 | 401 = not authenticated; 403 = authenticated but forbidden | Same semantics |

---

## 1. Role-based access in one attribute

```csharp
[HttpPost("transfer")]
[Authorize(Roles = "Teller,Manager")]   // comma-separated = OR
public IActionResult Transfer(...) { ... }
```

---

## 2. Custom IAuthorizationRequirement

Useful when the "can access?" logic depends on **runtime data** (the specific resource being accessed), not just a static role.

```csharp
// 1. Marker requirement (no extra properties needed here)
public class AccountOwnerRequirement : IAuthorizationRequirement { }

// 2. Handler checks role OR ownership
public class AccountOwnerHandler
    : AuthorizationHandler<AccountOwnerRequirement, BankingResource>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        AccountOwnerRequirement     requirement,
        BankingResource             resource)
    {
        if (context.User.IsInRole("Manager"))       // managers can do everything
            context.Succeed(requirement);
        else if (context.User.FindFirstValue(ClaimTypes.Name) == resource.OwnerId)
            context.Succeed(requirement);           // owner can close own account
        return Task.CompletedTask;
    }
}

// 3. Register in Program.cs
services.AddAuthorization(o =>
    o.AddPolicy("AccountOwner", p => p.Requirements.Add(new AccountOwnerRequirement())));
services.AddScoped<IAuthorizationHandler, AccountOwnerHandler>();

// 4. Use in controller
var result = await _authz.AuthorizeAsync(User, resource, "AccountOwner");
if (!result.Succeeded) return Forbid();
```

**Java parallel:**
```java
@PreAuthorize("@bankingSecurity.canClose(authentication, #id)")
@DeleteMapping("/accounts/{id}")
public ResponseEntity<?> closeAccount(@PathVariable Long id) { ... }

@Component
public class BankingSecurity {
    public boolean canClose(Authentication auth, Long id) {
        return auth.getAuthorities().stream().anyMatch(a -> a.getAuthority().equals("ROLE_MANAGER"))
            || auth.getName().equals(id.toString());
    }
}
```

---

## Project Structure (new / changed files)

```
Lesson/
  Controllers/
    BankingAuthController.cs  NEW  GET /banking/balance, POST /banking/transfer,
                                   DELETE /banking/accounts/{id},
                                   AccountOwnerRequirement, AccountOwnerHandler,
                                   BankingResource, TransferRequest
  Program.cs                       + AddAuthorization policy "AccountOwner",
                                   + AddScoped<IAuthorizationHandler, AccountOwnerHandler>
Lesson.Tests/
  RoleBasedAuthTests.cs       NEW  8 integration tests (role guard, custom policy, 401 vs 403)
```

---

## Tests

```bash
dotnet test --filter "FullyQualifiedName~RoleBasedAuthTests"
# 8 tests — all pass
```

---

## Exercises

1. Add a `Guest` role to the in-memory user store and test that a Guest gets 403 on `POST /banking/transfer`.
2. Extract `AccountOwnerHandler` into its own file under `Lesson/Authorization/` — this is the real-world placement pattern.
3. Add a `MinimumBalanceRequirement(decimal minimum)` that only allows transfers when the caller's account balance exceeds a threshold — pass the requirement a balance via the resource object.


> **Branch:** `lesson/13-auth-jwt/a-basic`
> **Prerequisites:** Lesson 12-C (integration tests)

---

## What you will learn

| Topic | C# ASP.NET Core | Java Spring Security |
|---|---|---|
| Add JWT bearer scheme | `AddAuthentication().AddJwtBearer(...)` | `@EnableWebSecurity` + `JwtAuthenticationFilter` |
| Protect endpoint | `[Authorize]` | `@PreAuthorize` / `SecurityConfig.authorizeRequests()` |
| Allow anonymous | `[AllowAnonymous]` | `permitAll()` |
| Read identity | `HttpContext.User.Identity?.Name` | `SecurityContextHolder.getContext().getAuthentication()` |
| Read specific claim | `User.FindFirstValue(ClaimTypes.Role)` | `authentication.getAuthorities()` |
| Issue a token | `JwtSecurityTokenHandler().WriteToken(...)` | JJWT `Jwts.builder()...compact()` |
| Validate in middleware | Handled automatically by `UseAuthentication()` | `OncePerRequestFilter` reading `Authorization` header |

---

## 1. Token pipeline

```
POST /auth/login
  ? validate credentials (BCrypt)
  ? build JwtSecurityToken (claims + signing key)
  ? return { token, expiresIn }

GET /auth/me  (Authorization: Bearer <token>)
  ? UseAuthentication() middleware validates signature + expiry
  ? if valid, populates HttpContext.User with claims
  ? [Authorize] attribute allows the request through
  ? controller reads User.Identity.Name and ClaimTypes.Role
```

---

## 2. Issuing a token

```csharp
var key  = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey)); // ? 32 bytes
var cred = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

var token = new JwtSecurityToken(
    issuer:   "MyApp",
    audience: "MyApp",
    claims:   [new Claim(ClaimTypes.Name, "alice"), new Claim(ClaimTypes.Role, "Teller")],
    expires:  DateTime.UtcNow.AddHours(1),
    signingCredentials: cred);

return new JwtSecurityTokenHandler().WriteToken(token);
```

---

## 3. Registration in Program.cs

```csharp
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer           = true,
            ValidateAudience         = true,
            ValidateLifetime         = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer              = "MyApp",
            ValidAudience            = "MyApp",
            IssuerSigningKey         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey)),
        };
    });
builder.Services.AddAuthorization();

// In pipeline — ORDER MATTERS: Authentication before Authorization
app.UseAuthentication();
app.UseAuthorization();
```

**Java parallel:**
```java
@Bean
SecurityFilterChain filterChain(HttpSecurity http) throws Exception {
    return http
        .authorizeHttpRequests(a -> a.requestMatchers("/auth/login").permitAll().anyRequest().authenticated())
        .addFilterBefore(new JwtAuthFilter(secretKey), UsernamePasswordAuthenticationFilter.class)
        .build();
}
```

---

## Project Structure (new / changed files)

```
Lesson/
  Controllers/
    JwtAuthController.cs   NEW  POST /auth/login, GET /auth/me, GET /auth/profile, JwtOptions
  appsettings.json               + "Jwt" section (SecretKey, Issuer, Audience, ExpirySeconds)
  Program.cs                     + JwtOptions wiring, AddAuthentication/AddJwtBearer, UseAuthentication
Lesson.Tests/
  JwtAuthTests.cs          NEW  10 integration tests (login, 401 for bad creds, 401 without token,
                                claims assertions, tampered token rejection)
```

---

## Tests

```bash
dotnet test --filter "FullyQualifiedName~JwtAuthTests"
# 10 tests — all pass
```

---

## Exercises

1. Add a `GET /auth/admin` endpoint decorated with `[Authorize(Roles = "Manager")]` and write tests that assert a Teller gets 403 and a Manager gets 200.
2. Add an `ExpirySeconds = 1` test-only JwtOptions override in the factory — wait 2 seconds, then assert an expired token returns 401.
3. Decode the JWT without a library: split the token on `.`, Base64Url-decode the middle part, and pretty-print the JSON payload — observe the `sub`, `role`, `exp`, and `jti` claims.


> **Branch:** `lesson/12-unit-testing/c-advanced`
> **Prerequisites:** Lesson 12-B (Moq, FluentAssertions)

---

## What you will learn

| Topic | C# .NET | Java (Spring) |
|---|---|---|
| Integration test host | `WebApplicationFactory<Program>` | `@SpringBootTest` + `MockMvc` |
| Share host across tests | `IClassFixture<TFactory>` | `@SpringBootTest` (singleton context) |
| Swap real DB for test DB | `ConfigureWebHost` + `UseSqlite(":memory:")` | `@DataJpaTest` / H2 in-memory |
| Persistent in-memory DB | `SqliteConnection` kept open in factory | H2 with `spring.datasource.url=mem:...` |
| Seed DB directly | `IServiceScope` + `BankingDbContext` | `@Sql` or `EntityManager` in `@BeforeEach` |
| Code coverage | `coverlet.collector` + `dotnet test --collect:"XPlat Code Coverage"` | Jacoco |

---

## 1. WebApplicationFactory — the core concept

`WebApplicationFactory<Program>` starts the real ASP.NET Core pipeline in memory — no network, no ports — and gives you an `HttpClient` pre-wired to it.

```csharp
public class MyFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public MyFactory() => _connection.Open();   // keep alive for full test run

    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.ConfigureServices(services =>
        {
            // Remove the real DB registration
            var d = services.Single(s => s.ServiceType == typeof(DbContextOptions<BankingDbContext>));
            services.Remove(d);

            // Add in-memory SQLite using the SAME connection
            services.AddDbContext<BankingDbContext>(o => o.UseSqlite(_connection));

            // Run migrations once
            var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            scope.ServiceProvider.GetRequiredService<BankingDbContext>().Database.Migrate();
        });
}
```

**Java parallel:**
```java
@SpringBootTest(webEnvironment = RANDOM_PORT)
@AutoConfigureTestDatabase(replace = Replace.ANY)
class AccountsIntegrationTest { ... }
```

---

## 2. IClassFixture — sharing the host

```csharp
public class AccountsIntegrationTests : IClassFixture<AccountsTestFactory>
{
    private readonly HttpClient _client;

    public AccountsIntegrationTests(AccountsTestFactory factory)
        => _client = factory.CreateClient();
}
```

xUnit creates **one** `AccountsTestFactory` and injects it into every test constructor — exactly like Spring's shared application context.

---

## 3. Seeding the database directly

```csharp
using var scope = _factory.Services.CreateScope();
var db = scope.ServiceProvider.GetRequiredService<BankingDbContext>();
db.BankAccounts.Add(new BankAccount { ... });
await db.SaveChangesAsync();
```

Then verify via HTTP:
```csharp
var list = await _client.GetFromJsonAsync<List<AccountResponse>>("/accounts");
list.Should().Contain(a => a.AccountNumber == "SEED-001");
```

---

## 4. Code coverage

```bash
dotnet test --collect:"XPlat Code Coverage"
# Produces coverage.cobertura.xml in TestResults/

# Generate HTML report (requires reportgenerator tool)
dotnet tool install -g dotnet-reportgenerator-globaltool
reportgenerator -reports:"**/coverage.cobertura.xml" -targetdir:"coveragereport" -reporttypes:Html
```

---

## Project Structure (new / changed files)

```
Lesson.Tests/
  AccountsIntegrationTests.cs  NEW  7 full-stack integration tests + AccountsTestFactory
Lesson.Tests/Lesson.Tests.csproj   + coverlet.collector 6.0.4
```

---

## Tests

```bash
dotnet test --filter "FullyQualifiedName~AccountsIntegrationTests"
# 7 tests — all pass
```

---

## Exercises

1. Add a test that creates 10 accounts and then calls `GET /accounts?type=Savings` — assert only savings accounts are returned.
2. Test optimistic concurrency: create an account, update it once from `_client`, update it a second time from a *second* `HttpClient` using the original `RowVersion` — expect `409 Conflict`.
3. Run `dotnet test --collect:"XPlat Code Coverage"` and open the HTML report — find which controller methods are not yet covered.


> **Branch:** `lesson/12-unit-testing/b-intermediate`
> **Prerequisites:** Lesson 12-A (xUnit basics, AAA pattern)

---

## What you will learn

| Topic | C# | Java (Mockito) |
|---|---|---|
| Create mock | `new Mock<IFoo>()` | `Mockito.mock(IFoo.class)` |
| Define return value | `.Setup(m => m.Get(1)).ReturnsAsync(x)` | `when(m.get(1)).thenReturn(x)` |
| Wildcard matcher | `It.IsAny<int>()` | `any(Integer.class)` |
| Verify call count | `.Verify(m => m.Get(1), Times.Once())` | `verify(m, times(1)).get(1)` |
| Verify never called | `Times.Never()` | `verify(m, never()).get(0)` |
| Fluent equality | `result.Should().Be(42)` | `assertThat(result).isEqualTo(42)` |
| Null check | `.Should().BeNull()` / `.NotBeNull()` | `.isNull()` / `.isNotNull()` |
| Collection count | `.Should().HaveCount(3)` | `hasSize(3)` |
| Exception assertion | `act.Should().Throw<Ex>().WithMessage(...)` | `assertThatThrownBy(...).isInstanceOf(...).hasMessage(...)` |

---

## 1. Mocking a repository

```csharp
var mock = new Mock<IAccountRepository>();

// Define what the mock returns
mock.Setup(r => r.GetByIdAsync(42))
    .ReturnsAsync(new BankAccount { Id = 42, Balance = 1_200m });

// Use .Object to get the real interface instance
var repo   = mock.Object;
var result = await repo.GetByIdAsync(42);

result.Should().NotBeNull();
result!.Balance.Should().Be(1_200m);
```

**Java parallel:**
```java
var mock = Mockito.mock(IAccountRepository.class);
when(mock.getByIdAsync(42)).thenReturn(Optional.of(account));
var result = mock.getByIdAsync(42).get();
assertThat(result.getBalance()).isEqualTo(1200.0);
```

---

## 2. Verifying interactions

```csharp
mock.Verify(r => r.AddAsync(account), Times.Once());    // must be called exactly once
mock.Verify(r => r.GetByIdAsync(0),   Times.Never());   // must NOT be called with 0
```

**Java parallel:**
```java
verify(mock, times(1)).save(account);
verify(mock, never()).findById(0);
```

---

## 3. FluentAssertions

Standard `Assert.Equal(a, b)` is inside-out (expected first). FluentAssertions reads left-to-right:

```csharp
account.Balance.Should().Be(1_500m, because: "500 + 1000 = 1500");

list.Should().HaveCount(3)
    .And.Contain(a => a.Id == 2)
    .And.AllSatisfy(a => a.IsActive.Should().BeTrue());

var act = () => svc.Withdraw(account, 999m);
act.Should().Throw<InvalidOperationException>()
   .WithMessage("Insufficient funds.");
```

> **Note:** FluentAssertions 8.x requires a paid license for commercial use. For open-source and learning projects it is free. An alternative is `Shouldly` (MIT licensed).

---

## Project Structure (new / changed files)

```
Lesson.Tests/
  MockedAccountRepositoryTests.cs  NEW  10 tests with Moq + FluentAssertions
Lesson.Tests/Lesson.Tests.csproj        + Moq 4.20.72, FluentAssertions 8.4.0
```

---

## Tests

```bash
dotnet test --filter "FullyQualifiedName~MockedAccountRepositoryTests"
# 10 tests — all pass
```

---

## Exercises

1. Mock `IUnitOfWork` and write a test that calls `CommitAsync` twice — verify with `Times.Exactly(2)`.
2. Use `mock.SetupSequence(...)` to return different values on successive calls to `GetByIdAsync` — model a retry scenario.
3. Replace the `FluentAssertions` package with `Shouldly` and compare the assertion API.


> **Branch:** `lesson/12-unit-testing/a-basic`
> **Prerequisites:** None (pure unit tests, no framework setup needed)

---

## What you will learn

| Topic | C# xUnit | Java JUnit 5 |
|---|---|---|
| Single test | `[Fact]` | `@Test` |
| Parameterised test | `[Theory]` + `[InlineData]` | `@ParameterizedTest` + `@ValueSource` / `@CsvSource` |
| Assert equality | `Assert.Equal(expected, actual)` | `assertEquals(expected, actual)` |
| Assert true/false | `Assert.True(...)` / `Assert.False(...)` | `assertTrue(...)` / `assertFalse(...)` |
| Assert throws | `Assert.Throws<TEx>(() => ...)` | `assertThrows(Type.class, () -> ...)` |
| Arrange/Act/Assert | Pattern — comment or blank-line separated sections | Same pattern in Java |
| Test naming | `Method_Scenario_ExpectedResult` | Same convention |

---

## 1. The AAA pattern

Every test follows three phases:

```csharp
[Fact]
public void Deposit_PositiveAmount_IncreasesBalance()
{
    // Arrange — set up the objects under test
    var account = new BankAccount { Balance = 100m };
    var svc     = new BankAccountDomainService();

    // Act — call the code being tested
    svc.Deposit(account, 50m);

    // Assert — verify the outcome
    Assert.Equal(150m, account.Balance);
}
```

**Java parallel:**
```java
@Test
void deposit_positiveAmount_increasesBalance() {
    // Arrange
    var account = new BankAccount(100.0);
    var svc     = new BankAccountDomainService();
    // Act
    svc.deposit(account, 50.0);
    // Assert
    assertEquals(150.0, account.getBalance());
}
```

---

## 2. [Theory] with [InlineData]

Run the same test body with multiple input sets — no copy-paste.

```csharp
[Theory]
[InlineData(0)]
[InlineData(-1)]
[InlineData(-100)]
public void Deposit_NonPositiveAmount_Throws(decimal amount)
{
    var account = new BankAccount { Balance = 100m };
    Assert.Throws<ArgumentOutOfRangeException>(() => svc.Deposit(account, amount));
}
```

**Java parallel:**
```java
@ParameterizedTest
@ValueSource(doubles = {0, -1, -100})
void deposit_nonPositiveAmount_throws(double amount) {
    assertThrows(IllegalArgumentException.class,
        () -> svc.deposit(account, amount));
}
```

---

## 3. Pure unit tests vs integration tests

| | Pure unit test (this lesson) | Integration test (12-C) |
|---|---|---|
| Speed | < 1 ms per test | 100 ms+ (DB, HTTP) |
| Dependencies | None — created directly | DB, HTTP, DI container |
| What you test | Business logic, algorithms | Wired-together components |
| When to use | Domain rules, calculations, validations | Controller ? DB flow, auth, file IO |

---

## Project Structure (new / changed files)

```
Lesson/
  Domain/
    BankAccountDomainService.cs   NEW  Deposit, Withdraw, CanClose, interest calc
Lesson.Tests/
  BankAccountDomainTests.cs       NEW  18 tests (8 Fact + 10 Theory/InlineData)
```

---

## Tests

```bash
dotnet test --filter "FullyQualifiedName~BankAccountDomainTests"
# 18 tests — all pass in < 200 ms
```

---

## Exercises

1. Add a `Transfer(from, to, amount)` method to `BankAccountDomainService` and write tests for: happy path, insufficient funds, negative amount.
2. Add a `[MemberData]` test that reads test cases from a `public static IEnumerable<object[]>` property — useful when `[InlineData]` values are too complex (e.g. full objects).
3. Add a test that verifies `GenerateAccountNumber` never produces duplicates when called with 100 sequential values.


> **Branch:** `lesson/11-encryption/c-advanced`
> **Prerequisites:** Lesson 11-B (AES, key/IV management)

---

## What you will learn

| Topic | C# .NET | Java parallel |
|---|---|---|
| `IDataProtectionProvider` | root factory for protectors | Jasypt `StandardPBEStringEncryptor` |
| `IDataProtector.Protect` | encrypt + authenticate a string | `encryptor.encrypt(value)` |
| `IDataProtector.Unprotect` | decrypt and verify | `encryptor.decrypt(value)` |
| **Purpose strings** | isolate key rings per feature | Jasypt password + SaltGenerator |
| `ITimeLimitedDataProtector` | token with built-in expiry | Hand-rolled JWT `exp` claim |
| Key ring | auto-managed, auto-rotated key store | Jasypt `password` property |
| Tamper detection | built-in HMAC — throws on any modification | — |

---

## 1. Why Data Protection instead of raw AES?

| | Raw AES (Lesson 11-B) | Data Protection API |
|---|---|---|
| Key management | You manage key, IV, rotation | Automatic key generation + rotation |
| Authentication | None (CBC mode) | Built-in (HMAC) — detects tampering |
| Purpose isolation | Manual | `CreateProtector("PurposeName")` |
| Token expiry | Not built in | `ITimeLimitedDataProtector` |
| Production storage | You choose | File system ? Azure Blob ? Key Vault |
| Jasypt similarity | Low | **High** — same one-liner API |

Use Data Protection for **application data** (tokens, cookies, config values).
Use raw AES when you need **interoperability** with other systems (e.g. encrypt for a Java service).

---

## 2. Basic protect / unprotect

```csharp
// Injected: IDataProtectionProvider dpProvider

// Purpose string creates an isolated sub-key
IDataProtector protector = dpProvider.CreateProtector("BankingApp.AccountTokens");

string token     = protector.Protect("AccountId:42");
string plaintext = protector.Unprotect(token);   // "AccountId:42"
```

**Java parallel (Jasypt):**
```java
StandardPBEStringEncryptor enc = new StandardPBEStringEncryptor();
enc.setPassword(password);
String encrypted = enc.encrypt(value);
String decrypted = enc.decrypt(encrypted);
```

---

## 3. Purpose isolation

```csharp
var passwordResetProtector = dpProvider.CreateProtector("PasswordReset");
var sessionProtector       = dpProvider.CreateProtector("Session");

var token = passwordResetProtector.Protect("user@bank.com");

// This will throw CryptographicException — wrong purpose
sessionProtector.Unprotect(token);
```

Purpose strings act like namespaces: data protected with one purpose **cannot** be read by another, even though the same underlying key ring is used.

---

## 4. Time-limited tokens

```csharp
ITimeLimitedDataProtector tl = dpProvider
    .CreateProtector("PasswordReset")
    .ToTimeLimitedDataProtector();

// Create — valid for 15 minutes
string token = tl.Protect("user@bank.com", DateTimeOffset.UtcNow.AddMinutes(15));

// Consume — throws CryptographicException if expired
string payload = tl.Unprotect(token, out DateTimeOffset expiry);
```

Ideal for: password-reset links, email verification codes, one-time tokens.

---

## 5. Registration

```csharp
// Program.cs
builder.Services.AddDataProtection();
// Production: .PersistKeysToAzureBlobStorage(blobClient)
//             .ProtectKeysWithAzureKeyVault(keyVaultUri, new DefaultAzureCredential())
```

---

## Endpoints

| Method | Route | Notes |
|--------|-------|-------|
| `POST` | `/crypto/dp/protect` | Protect a string with an optional purpose |
| `POST` | `/crypto/dp/unprotect` | Unprotect; `400` on wrong purpose or tampered token |
| `POST` | `/crypto/dp/token/create` | Create a time-limited token (TTL in seconds) |
| `POST` | `/crypto/dp/token/consume` | Consume token; `400` if expired or tampered |

---

## Project Structure (new / changed files)

```
Lesson/
  Controllers/
    DataProtectionController.cs  NEW  /crypto/dp/* endpoints
  Program.cs                          + AddDataProtection()
Lesson.Tests/
  DataProtectionTests.cs         NEW  8 tests + DataProtectionTestFactory
```

---

## Tests

```bash
dotnet test --filter "FullyQualifiedName~DataProtectionTests"
# 8 tests — all pass
```

---

## Exercises

1. Add `PersistKeysToFileSystem(new DirectoryInfo("/tmp/dp-keys"))` to `AddDataProtection()` — restart the app twice and verify that tokens survive restarts.
2. Call `Protect` with purpose `"A"` in one test, then attempt `Unprotect` with purpose `"B"` — confirm the `400` response.
3. Simulate Jasypt: read an `EncryptedPassword` string from `appsettings.json` and decrypt it at startup using `IDataProtector`, injecting the plaintext password into an `IOptions<T>` object.
4. Extend the `ConsumeToken` endpoint to return the remaining TTL as seconds — compute it from `expiry - DateTimeOffset.UtcNow`.


> **Branch:** `lesson/11-encryption/b-intermediate`
> **Prerequisites:** Lesson 11-A (Base64, hashing, BCrypt)

---

## What you will learn

| Topic | C# .NET | Java parallel |
|---|---|---|
| AES | `Aes.Create()` | `Cipher.getInstance("AES/CBC/PKCS5Padding")` |
| Key generation | `aes.GenerateKey()` | `KeyGenerator.generateKey()` |
| IV generation | `aes.GenerateIV()` | `cipher.init(ENCRYPT_MODE, key)` auto-IV |
| CBC mode | `aes.Mode = CipherMode.CBC` | `"AES/CBC/PKCS5Padding"` |
| Encrypt | `aes.CreateEncryptor().TransformFinalBlock(…)` | `cipher.doFinal(bytes)` |
| Decrypt | `aes.CreateDecryptor().TransformFinalBlock(…)` | `cipher.doFinal(cipherBytes)` |
| Key sizes | 128 / 192 / 256 bits | Same |

---

## 1. AES fundamentals

AES is a **symmetric** cipher — the same key is used to encrypt and decrypt.

| Term | Description |
|---|---|
| Key | 128/192/256-bit secret; must be protected (vault, not config file) |
| IV | 16-byte random value; generated fresh per message; public (travels with ciphertext) |
| Mode | CBC chains blocks; GCM adds authentication tag (prefer GCM for new code) |
| Padding | PKCS7 pads the last block to 16 bytes |

---

## 2. Encrypt

```csharp
using var aes  = Aes.Create();
aes.Key        = key;           // 32-byte (256-bit)
aes.Mode       = CipherMode.CBC;
aes.GenerateIV();               // cryptographically random each call

using var encryptor  = aes.CreateEncryptor();
var cipherBytes      = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

// Send both IV and ciphertext to the receiver (IV is NOT secret)
```

**Java parallel:**
```java
Cipher cipher = Cipher.getInstance("AES/CBC/PKCS5Padding");
cipher.init(Cipher.ENCRYPT_MODE, secretKey);
byte[] iv         = cipher.getIV();
byte[] cipherText = cipher.doFinal(plainBytes);
```

---

## 3. Decrypt

```csharp
using var aes  = Aes.Create();
aes.Key        = key;
aes.IV         = iv;            // same IV that was used during encryption
aes.Mode       = CipherMode.CBC;

using var decryptor = aes.CreateDecryptor();
var plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
```

---

## 4. Why the IV matters

Encrypting the same plaintext twice **with the same IV** produces the same ciphertext — leaking that the values are identical (ECB vulnerability).
Generating a fresh random IV every time prevents this, at zero cost.

---

## Endpoints

| Method | Route | Notes |
|--------|-------|-------|
| `GET` | `/crypto/aes/generate-key?bits=256` | Generate a random AES key |
| `POST` | `/crypto/aes/encrypt` | Encrypt plaintext; returns `ciphertext` + `iv` (both Base64) |
| `POST` | `/crypto/aes/decrypt` | Decrypt; needs `ciphertextBase64`, `ivBase64`, `keyBase64` |

---

## Project Structure (new / changed files)

```
Lesson/
  Controllers/
    AesController.cs         NEW  /crypto/aes/* endpoints
Lesson.Tests/
  AesEncryptionTests.cs      NEW  9 tests (3 Theory + 6 Fact) + AesTestFactory
```

---

## Tests

```bash
dotnet test --filter "FullyQualifiedName~AesEncryptionTests"
# 9 tests — all pass
```

---

## Exercises

1. Switch to **AES-GCM** (`AesGcm`) — it adds an authentication tag that detects tampering. Verify that modifying a byte of the ciphertext causes `AuthenticationTagMismatchException`.
2. Implement a helper that **prepends the IV** to the ciphertext bytes so only one Base64 string needs to be stored, and split it on decrypt.
3. Store the AES key in `IOptions<T>` and read it from User Secrets — simulate how production apps manage symmetric keys.
4. Benchmark AES-128 vs AES-256 for 1 MB of data using `Stopwatch` — the difference is small.


> **Branch:** `lesson/11-encryption/a-basic`
> **Prerequisites:** Lesson 10 (file handling)

---

## What you will learn

| Topic | C# .NET | Java parallel |
|---|---|---|
| Base64 encode/decode | `Convert.ToBase64String` / `Convert.FromBase64String` | `Base64.getEncoder().encodeToString` |
| SHA-256 hashing | `SHA256.HashData(bytes)` | `MessageDigest.getInstance("SHA-256")` |
| HMAC-SHA256 | `new HMACSHA256(key)` | `Mac.getInstance("HmacSHA256")` |
| Constant-time comparison | `CryptographicOperations.FixedTimeEquals` | `MessageDigest.isEqual` |
| Password hashing | `BCrypt.Net.BCrypt.HashPassword` | `BCryptPasswordEncoder` (Spring Security) |
| Password verification | `BCrypt.Net.BCrypt.Verify` | `BCryptPasswordEncoder.matches` |

---

## 1. Base64

Base64 encodes arbitrary bytes as printable ASCII text — useful for embedding binary data in JSON/HTTP.
It is **not** encryption — it is trivially reversible.

```csharp
var bytes   = Encoding.UTF8.GetBytes("Hello, Bank!");
var encoded = Convert.ToBase64String(bytes);          // "SGVsbG8sIEJhbmsh"
var decoded = Encoding.UTF8.GetString(
                Convert.FromBase64String(encoded));   // "Hello, Bank!"
```

---

## 2. SHA-256 — one-way hash

Good for: document fingerprints, integrity checks, checksums.
**Bad for passwords** — it's too fast; GPUs can compute billions/second.

```csharp
var bytes = Encoding.UTF8.GetBytes(input);
var hash  = SHA256.HashData(bytes);          // static, no allocation
var hex   = Convert.ToHexString(hash).ToLowerInvariant();
```

SHA-256 is deterministic — same input always produces same output.

---

## 3. HMAC-SHA256 — keyed hash

HMAC adds a **secret key** to the hash. Useful for API request signing and JWT secrets.
Only parties with the key can verify the signature.

```csharp
using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
```

**Always compare MAC values with constant-time equality** to prevent timing attacks:
```csharp
CryptographicOperations.FixedTimeEquals(expected, actual)
```

---

## 4. BCrypt — password hashing

BCrypt is intentionally slow (configurable work factor) to make brute-force expensive.
It generates and stores a random salt internally.

```csharp
// Hash (work factor 11 = ~100ms on a modern CPU)
var hash = BCrypt.Net.BCrypt.HashPassword(password, workFactor: 11);

// Verify — automatically extracts salt from the hash string
var valid = BCrypt.Net.BCrypt.Verify(password, hash);
```

**Never use SHA-256 for passwords.** Always use bcrypt, argon2, or scrypt.

---

## Endpoints

| Method | Route | Notes |
|--------|-------|-------|
| `POST` | `/crypto/base64/encode` | UTF-8 text ? Base64 |
| `POST` | `/crypto/base64/decode` | Base64 ? UTF-8 text |
| `POST` | `/crypto/sha256` | SHA-256 hash (hex + base64) |
| `POST` | `/crypto/hmac` | HMAC-SHA256 (hex + base64) |
| `POST` | `/crypto/hmac/verify` | Constant-time HMAC check |
| `POST` | `/crypto/password/hash` | BCrypt hash |
| `POST` | `/crypto/password/verify` | BCrypt verify |

---

## Project Structure (new / changed files)

```
Lesson/
  Controllers/
    CryptoBasicController.cs  NEW  /crypto/* endpoints
Lesson.Tests/
  CryptoBasicTests.cs         NEW  11 tests + CryptoTestFactory
```

---

## Tests

```bash
dotnet test --filter "FullyQualifiedName~CryptoBasicTests"
# 11 tests — all pass
```

---

## Exercises

1. Add a `GET /crypto/sha256/file?path=…` endpoint that hashes a file's bytes — useful for verifying download integrity.
2. Benchmark BCrypt at work factors 10, 12, and 14 — observe the exponential time increase.
3. Add an endpoint that computes SHA-256 of a streaming request body using `SHA256.Create()` and `CryptoStream`.
4. Explain why `string.Equals(a, b)` is unsafe for comparing HMACs.


> **Branch:** `lesson/10-file-handling/b-intermediate`
> **Prerequisites:** Lesson 10-A (File, StreamReader/StreamWriter, FileStream)

---

## What you will learn

| Topic | C# .NET | Java parallel |
|---|---|---|
| `IFormFile` | ASP.NET Core multipart upload abstraction | `@RequestParam MultipartFile file` |
| `[Consumes("multipart/form-data")]` | document accepted content types | `@PostMapping(consumes = MediaType.MULTIPART_FORM_DATA_VALUE)` |
| `CsvHelper` | third-party CSV reader/writer | OpenCSV / Apache Commons CSV |
| `CsvReader.GetRecordsAsync<T>` | async streaming CSV parse | — |
| `[Name]` attribute | map CSV column by name | `@CsvBindByName` (OpenCSV) |
| `JsonSerializer.SerializeAsync` | async JSON ? stream | `ObjectMapper.writeValue(OutputStream, …)` |
| `JsonSerializer.DeserializeAsync` | async stream ? JSON | `ObjectMapper.readValue(InputStream, …)` |

---

## 1. IFormFile — file upload

```csharp
[HttpPost("import")]
[Consumes("multipart/form-data")]
public async Task<IActionResult> ImportCsv(IFormFile file, CancellationToken ct)
{
    using var reader = new StreamReader(file.OpenReadStream());
    // …
}
```

`IFormFile.OpenReadStream()` gives the raw byte stream without saving to disk.
Key properties: `FileName`, `Length`, `ContentType`, `OpenReadStream()`.

**Java parallel:**
```java
@PostMapping(consumes = MediaType.MULTIPART_FORM_DATA_VALUE)
public ResponseEntity<?> upload(@RequestParam("file") MultipartFile file) {
    try (var is = file.getInputStream()) { … }
}
```

---

## 2. CsvHelper — parsing CSV

```csharp
// Map columns by name using [Name] attribute on the record class
public record TransactionCsvRecord
{
    [Name("account_id")] public string AccountId { get; init; } = "";
    [Name("amount")]     public decimal Amount   { get; init; }
    // …
}

var config = new CsvConfiguration(CultureInfo.InvariantCulture) { HasHeaderRecord = true };
using var csv = new CsvReader(reader, config);

await foreach (var record in csv.GetRecordsAsync<TransactionCsvRecord>(ct))
    records.Add(record);
```

`GetRecordsAsync` returns an `IAsyncEnumerable<T>` — you can process each row without loading the full file into memory first.

---

## 3. System.Text.Json — async file IO

```csharp
// Write
await using var fs = new FileStream(path, FileMode.Create, …, useAsync: true);
await JsonSerializer.SerializeAsync(fs, payload, cancellationToken: ct);

// Read
await using var fs = new FileStream(path, FileMode.Open, …, useAsync: true);
var doc = await JsonSerializer.DeserializeAsync<JsonElement>(fs, cancellationToken: ct);
```

Both operations write/read directly from/to a `Stream` — no intermediate string allocation.

---

## Endpoints

| Method | Route | Notes |
|--------|-------|-------|
| `POST` | `/files/csv/import` | Upload a CSV file (`multipart/form-data`), returns parsed records |
| `GET` | `/files/csv/export` | Download a CSV file (`text/csv`) |
| `POST` | `/files/json/save` | Serialize a JSON body to a temp file |
| `GET` | `/files/json/load?path=…` | Deserialize a JSON file |

---

## Project Structure (new / changed files)

```
Lesson/
  FileHandling/
    TransactionCsvRecord.cs  NEW  CsvHelper class map with [Name] attributes
  Controllers/
    CsvFileController.cs     NEW  /files/csv/* and /files/json/* endpoints
Lesson.Tests/
  CsvFileTests.cs            NEW  8 integration tests + CsvTestFactory
```

---

## Tests

```bash
dotnet test --filter "FullyQualifiedName~CsvFileTests"
# 8 tests — all pass
```

---

## Exercises

1. Add a `[Required]` column validation: if `amount` is missing return `400` instead of silently defaulting to `0`.
2. Write a CSV export endpoint that streams directly to the response body using `Response.Body` — no `MemoryStream` intermediate.
3. Use `JsonSerializerOptions` with `PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower` and observe how the saved JSON changes.
4. Add a `POST /files/csv/export` that accepts a list of records as JSON body and returns a CSV download.


> **Branch:** `lesson/10-file-handling/a-basic`
> **Prerequisites:** Lesson 09 (scheduled tasks)

---

## What you will learn

| Topic | C# .NET | Java parallel |
|---|---|---|
| Static helpers | `File.Exists`, `File.Delete`, `File.AppendText`, `new FileInfo(path)` | `java.nio.file.Files`, `new File(path)` |
| `StreamWriter` | `new StreamWriter(path)`, `await using` | `PrintWriter`/`BufferedWriter` via `Files.newBufferedWriter` |
| `StreamReader` | `new StreamReader(path)`, `ReadLineAsync` | `BufferedReader` via `Files.newBufferedReader` |
| `FileStream` | low-level byte stream; explicit mode, access, share, buffer flags | `FileOutputStream` + `BufferedOutputStream` |
| `using` declaration | C# 8+ — disposes at end of enclosing scope; no extra braces needed | `try`-with-resources |
| `await using` | `IAsyncDisposable` — flushes buffers asynchronously before dispose | No direct equivalent |

---

## 1. StreamWriter — writing text

```csharp
// 'await using' flushes asynchronously; works because StreamWriter : IAsyncDisposable
await using var writer = new StreamWriter(path, append: false);
await writer.WriteLineAsync(line.AsMemory(), cancellationToken);
```

**`append: false`** — truncates and creates; `append: true` — opens or creates and seeks to end.

**Java parallel:**
```java
try (var writer = Files.newBufferedWriter(Path.of(path))) {
    writer.write(line);
    writer.newLine();
}
```

---

## 2. StreamReader — reading text

```csharp
// StreamReader does not implement IAsyncDisposable ? plain 'using'
using var reader = new StreamReader(path);
while (await reader.ReadLineAsync(ct) is { } line)
    lines.Add(line);
```

`ReadLineAsync` returns `null` at EOF — the `is { }` pattern filters nulls out cleanly.

---

## 3. FileStream — binary data

```csharp
await using var fs = new FileStream(
    path,
    FileMode.Create,       // create or overwrite
    FileAccess.Write,
    FileShare.None,        // no concurrent readers/writers
    bufferSize: 4096,
    useAsync: true);       // enables OS async I/O on Windows

await fs.WriteAsync(bytes, cancellationToken);
```

`useAsync: true` is important for `await fs.WriteAsync` to be truly asynchronous on Windows.

---

## 4. File / FileInfo static helpers

```csharp
File.Exists(path)              // bool
File.Delete(path)              // void — throws if missing
File.AppendText(path)          // StreamWriter opened for append
new FileInfo(path).Length      // file size in bytes
new FileInfo(path).LastWriteTimeUtc
```

---

## Endpoints

| Method | Route | Notes |
|--------|-------|-------|
| `POST` | `/files/export` | Write transaction list to a temp text file |
| `GET` | `/files/read?path=…` | Read all lines from a file |
| `POST` | `/files/append` | Append a single line to an existing file |
| `GET` | `/files/info?path=…` | File metadata (size, dates) |
| `DELETE` | `/files/delete?path=…` | Remove a file |
| `POST` | `/files/binary` | Write Base64-encoded bytes via `FileStream` |

---

## Project Structure (new / changed files)

```
Lesson/
  FileHandling/
    FileHandlingDtos.cs          NEW  Request record types
  Controllers/
    FileHandlingController.cs    NEW  /files/* endpoints
Lesson.Tests/
  FileHandlingBasicTests.cs      NEW  8 integration tests + FileHandlingTestFactory
```

---

## Tests

```bash
dotnet test --filter "FullyQualifiedName~FileHandlingBasicTests"
# 8 tests — all pass
```

---

## Exercises

1. Add a `GET /files/list?directory=…` endpoint that returns all `.txt` files in a directory using `Directory.EnumerateFiles`.
2. Change `WriteBinary` to use `await File.WriteAllBytesAsync(path, bytes, ct)` — compare the two approaches.
3. Read a large file using `ReadAllLinesAsync` vs the `StreamReader` loop — benchmark with `Stopwatch` to understand the tradeoff.
4. Add compression: wrap `FileStream` in a `GZipStream` (`System.IO.Compression`) and write compressed text.


> **Branch:** `lesson/09-scheduled-tasks/b-intermediate`
> **Prerequisites:** Lesson 09-A (PeriodicTimer, BackgroundService)

---

## What you will learn

| Topic | Quartz.NET | Java parallel |
|---|---|---|
| `IJob` | unit of work executed by the scheduler | `org.quartz.Job` |
| `IJobDetail` | metadata + data map for a job class | `JobDetail` (Quartz for Java) |
| `ITrigger` / cron trigger | when a job fires | `CronTrigger` |
| `IScheduler` | engine that manages triggers + executes jobs | `org.quartz.Scheduler` |
| `[DisallowConcurrentExecution]` | prevents overlapping executions | `@DisallowConcurrentExecution` |
| DI-integrated jobs | jobs resolved from `IServiceProvider` per execution — can receive scoped services | Spring Quartz `AutowireCapableBeanJobFactory` |
| `ISchedulerFactory.GetScheduler()` | obtain the running scheduler | `SchedulerFactory.getScheduler()` |
| Manual trigger | `scheduler.TriggerJob(key)` — fire on demand | `scheduler.triggerJob(key)` |

---

## 1. Defining an IJob

```csharp
[DisallowConcurrentExecution]
public class StatementGenerationJob(
    JobHistoryStore store,
    ILogger<StatementGenerationJob> logger) : IJob
{
    public static readonly JobKey Key = new("StatementGeneration", "Banking");

    public async Task Execute(IJobExecutionContext context)
    {
        // context.FireTimeUtc — when the trigger fired
        // context.CancellationToken — cancelled on graceful shutdown
        await DoWorkAsync(context.CancellationToken);
    }
}
```

`[DisallowConcurrentExecution]` tells Quartz not to start a new execution until the previous one completes — equivalent to `@DisallowConcurrentExecution` in Java Quartz.

---

## 2. Registration in Program.cs

```csharp
builder.Services.AddQuartz(q =>
{
    q.AddJob<StatementGenerationJob>(opts => opts.WithIdentity(StatementGenerationJob.Key));

    q.AddTrigger(opts => opts
        .ForJob(StatementGenerationJob.Key)
        .WithIdentity("StatementTrigger", "Banking")
        .WithCronSchedule("0 * * * * ?")   // every minute
        .StartNow());
});

builder.Services.AddQuartzHostedService(opts =>
    opts.WaitForJobsToComplete = true);    // graceful shutdown
```

`AddQuartz` registers the Quartz scheduler as a DI service.
`AddQuartzHostedService` starts it as an `IHostedService`.

**Java parallel:** `spring-boot-starter-quartz` + `@Configuration` with `JobDetailFactoryBean` + `CronTriggerFactoryBean`.

---

## 3. Cron expressions

| Expression | Meaning |
|---|---|
| `0 * * * * ?` | Every minute at second 0 |
| `0 0 2 * * ?` | Every day at 02:00 |
| `0 0/15 * * * ?` | Every 15 minutes |
| `0 0 9-17 ? * MON-FRI` | Every hour 9 AM–5 PM, weekdays |

Quartz.NET cron has **6 fields** (seconds included), unlike the 5-field Unix cron.

---

## 4. Manual trigger (on-demand execution)

```csharp
var scheduler = await schedulerFactory.GetScheduler(ct);
await scheduler.TriggerJob(StatementGenerationJob.Key, ct);
```

Useful for: batch job dashboards, retry buttons, testing.

---

## PeriodicTimer vs Quartz

| | `PeriodicTimer` (09-A) | Quartz.NET (09-B) |
|---|---|---|
| Scheduling | Fixed period from last tick | Cron / calendars / misfire policies |
| Concurrency control | Manual | `[DisallowConcurrentExecution]` |
| Persistence | None (in-memory) | Optional (ADO.NET job store) |
| Monitoring | Custom | Quartz dashboard, `ISchedulerFactory` |
| Use case | Simple recurring task | Enterprise scheduling, complex calendars |

---

## Endpoints

| Method | Route | Notes |
|--------|-------|-------|
| `POST` | `/quartz/trigger` | Fires `StatementGenerationJob` immediately |
| `GET` | `/quartz/history` | Returns job execution history |
| `DELETE` | `/quartz/history/reset` | Clears history (test helper) |

---

## Project Structure (new / changed files)

```
Lesson/
  Jobs/
    StatementGenerationJob.cs    NEW  IJob implementation + JobKey
  Controllers/
    QuartzJobController.cs       NEW  /quartz/* endpoints
  Program.cs                          + AddQuartz, AddQuartzHostedService
Lesson.Tests/
  QuartzJobTests.cs              NEW  8 integration tests + QuartzTestFactory
```

---

## Tests

```bash
dotnet test --filter "FullyQualifiedName~QuartzJobTests"
# 8 tests — all pass
```

> **Testing note:** Quartz uses a static logger provider that captures `LoggerFactory`
> at startup. Tests use a `QuartzTestFactory : WebApplicationFactory<Program>` subclass
> with `ConfigureWebHost` override — this ensures only one host is created per test run,
> avoiding the `ObjectDisposedException` that occurs with per-test `WithWebHostBuilder` calls.

---

## Exercises

1. Add a `JobDataMap` to the trigger with a `reportType` key and read it from `IJobExecutionContext.MergedJobDataMap` inside the job.
2. Change the cron to `"0/5 * * * * ?"` (every 5 seconds), run the app, and observe multiple executions in the history endpoint.
3. Remove `[DisallowConcurrentExecution]` and trigger the job twice rapidly — observe both runs appearing in "Running" state simultaneously.
4. Add a second `IJob` (`AuditReportJob`) with its own trigger and verify both jobs fire independently.


> **Branch:** `lesson/09-scheduled-tasks/a-basic`
> **Prerequisites:** Lesson 08 (events, pub/sub, Channel\<T\>)

---

## What you will learn

| Topic | C# .NET | Java parallel |
|---|---|---|
| `IHostedService` | long-running service managed by the generic host | `ApplicationRunner` / `CommandLineRunner` |
| `BackgroundService` | abstract base — override `ExecuteAsync(CancellationToken)` | `ThreadPoolTaskExecutor` loop |
| `PeriodicTimer` | modern, alloc-free, non-blocking periodic tick | `@Scheduled(fixedDelay = …)` |
| `CancellationToken` | graceful shutdown signal from the host | `Thread.interrupt()` |
| Singleton + IHostedService | register same instance as both singleton and hosted service | Spring `@Component` with `@Scheduled` method |

---

## 1. PeriodicTimer — the modern scheduling primitive

```csharp
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));

    while (await timer.WaitForNextTickAsync(stoppingToken))
    {
        // runs once per period
        await DoWorkAsync(stoppingToken);
    }
}
```

Key properties of `PeriodicTimer`:
- `WaitForNextTickAsync` suspends **without blocking a thread** — much more efficient than `Thread.Sleep` or `Task.Delay` loops.
- **Tick skipping**: if `DoWorkAsync` takes longer than the period, the *next* tick fires immediately once. Subsequent ticks resume on schedule. This prevents work piling up.
- Returns `false` when `stoppingToken` is cancelled ? clean loop exit.

**Java parallel:**
```java
@Scheduled(fixedDelay = 30_000)
public void doWork() { … }
```
The key difference: Spring creates a new thread per invocation; `PeriodicTimer` reuses the same `async` continuation on the thread pool.

---

## 2. Registering the same instance as singleton + IHostedService

```csharp
// Register the concrete type as a singleton so other services (e.g. controller) can inject it
builder.Services.AddSingleton<InterestCalculationService>(sp =>
    new InterestCalculationService(
        sp.GetRequiredService<JobHistoryStore>(),
        sp.GetRequiredService<ILogger<InterestCalculationService>>(),
        period: TimeSpan.FromSeconds(30)));

// Tell the host to start/stop this *same* instance
builder.Services.AddHostedService(sp => sp.GetRequiredService<InterestCalculationService>());
```

Registering the concrete type as a singleton first allows `ScheduledTasksController` to inject `InterestCalculationService` directly and read its execution log.

---

## 3. JobHistoryStore

```csharp
public sealed class JobHistoryStore
{
    private readonly List<JobExecution> _history = [];
    public IReadOnlyList<JobExecution> History => _history.AsReadOnly();

    public void Add(JobExecution run) { … }
    public void Update(Guid runId, JobExecution updated) { … }
    public void Clear() { … }
}
```

Singleton in-memory store — shared between the background service (writer) and the controller (reader).

---

## Endpoints

| Method | Route | Notes |
|--------|-------|-------|
| `GET` | `/scheduled-tasks/history` | Returns all recorded job runs |
| `DELETE` | `/scheduled-tasks/history/reset` | Clears the history (test helper) |

---

## Project Structure (new / changed files)

```
Lesson/
  ScheduledTasks/
    JobExecution.cs               NEW  Record — execution snapshot
    JobHistoryStore.cs            NEW  Singleton in-memory execution log
  HostedServices/
    InterestCalculationService.cs NEW  BackgroundService + PeriodicTimer
  Controllers/
    ScheduledTasksController.cs   NEW  /scheduled-tasks/* endpoints
  Program.cs                          + JobHistoryStore, InterestCalculationService registrations
Lesson.Tests/
  ScheduledTaskBasicTests.cs      NEW  7 tests (6 integration + 1 unit)
```

---

## Tests

```bash
dotnet test --filter "FullyQualifiedName~ScheduledTaskBasicTests"
# 7 tests — all pass
```

| Test | What it verifies |
|---|---|
| `GetHistory_Returns200` | Endpoint responds |
| `GetHistory_ReturnsEmptyArrayByDefault` | Clean initial state |
| `Reset_Returns204` | Reset endpoint |
| `AddToStore_AppearsInHistoryEndpoint` | Store ? controller integration |
| `AfterReset_HistoryIsEmpty` | Reset clears the store |
| `MultipleRuns_AllAppearInHistory` | Multiple entries accumulate |
| `InterestCalculationService_ExecutesOneTickAndRecordsCompletedRun` | Service actually runs, job completes, history updated |

> **Testing note:** Background service timing is non-deterministic in the test host.
> Integration tests verify the HTTP API and DI wiring. The one unit test drives
> `ExecuteAsync` directly with a short period and polls for completion.

---

## Exercises

1. Add a `LastRun` property to `JobHistoryStore` and expose it on a `GET /scheduled-tasks/last-run` endpoint.
2. Change from `UnboundedChannel` to a bounded channel with capacity 1 — observe that the timer skips ticks while the job is running.
3. Inject `IServiceScopeFactory` into `InterestCalculationService` and open a scoped `BankingDbContext` inside the tick loop to read real account data.
4. Add a `[Fact]` that checks a tick still fires after `StopAsync` is called with a 5-second timeout — verifying graceful shutdown.


> **Branch:** `lesson/08-events/c-advanced`
> **Prerequisites:** Lesson 08-B (MediatR INotification, fan-out pub/sub)

---

## What you will learn

| Topic | C# .NET | Java parallel |
|---|---|---|
| `IHostedService` | long-running background service managed by the host | `@Component` + `ApplicationRunner` / `Runnable` in a thread pool |
| `BackgroundService` | abstract base class — override `ExecuteAsync(CancellationToken)` | `ThreadPoolTaskExecutor` task that loops until interrupted |
| `CancellationToken` | signals graceful shutdown | `Thread.interrupt()` / `InterruptedException` |
| `Channel<T>` | thread-safe, lock-free async queue | `LinkedBlockingQueue<T>` / `ArrayBlockingQueue<T>` |
| `ChannelWriter<T>` | producer side — `WriteAsync`, `TryWrite` | `queue.put()` |
| `ChannelReader<T>` | consumer side — `ReadAllAsync`, `ReadAsync` | `queue.take()` |
| Bounded vs Unbounded | `Channel.CreateBounded` / `Channel.CreateUnbounded` | `ArrayBlockingQueue(N)` / `LinkedBlockingQueue()` |
| Outbox pattern intro | publish message ? queue ? background consumer | Transactional outbox with Kafka/RabbitMQ |

---

## 1. BackgroundService

```csharp
public class OutboxConsumerService(OutboxChannel channel, ILogger<...> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var message in channel.Reader.ReadAllAsync(stoppingToken))
        {
            // process each message
            await Task.Delay(5, stoppingToken);
            _processed.Add(message);
        }
    }
}
```

`ExecuteAsync` runs on a background thread.  
`ReadAllAsync` suspends (without blocking a thread) until a message is available or the token is cancelled.  
When the application stops, `stoppingToken` is cancelled ? `ReadAllAsync` throws `OperationCanceledException` ? the `await foreach` exits cleanly.

**Java parallel:**
```java
@Bean
public ApplicationRunner consumer(BlockingQueue<Message> queue) {
    return args -> {
        while (!Thread.currentThread().isInterrupted()) {
            Message msg = queue.take();   // blocks thread
            process(msg);
        }
    };
}
```
The key difference: `ReadAllAsync` releases the thread while waiting; `BlockingQueue.take()` blocks it.

---

## 2. Channel\<T\>

```csharp
// Unbounded — producer never waits; ideal when producer is bursty and consumer is reliable
var channel = Channel.CreateUnbounded<OutboxMessage>(
    new UnboundedChannelOptions { SingleReader = true });

// Producer
await channel.Writer.WriteAsync(message);   // completes immediately (unbounded)

// Consumer
await foreach (var msg in channel.Reader.ReadAllAsync(ct))
    Process(msg);
```

`SingleReader = true` is a performance hint — allows internal optimisations when only one consumer exists.

---

## 3. Registration in Program.cs

```csharp
// Singleton — the channel is shared between producer (controller) and consumer (hosted service)
builder.Services.AddSingleton<OutboxChannel>();

// Register the concrete type so OutboxController can inject it directly to read Processed log
builder.Services.AddSingleton<OutboxConsumerService>();
// Register the same instance as IHostedService so the host starts/stops it
builder.Services.AddHostedService(sp => sp.GetRequiredService<OutboxConsumerService>());
```

Registering the singleton separately and then as `IHostedService` via factory ensures that
`OutboxController` and the host share the **same instance** of the consumer.

---

## 4. Outbox Pattern (conceptual)

In a real outbox pattern the flow is:
```
Controller ? write to DB outbox table (same transaction as the business change)
BackgroundService ? poll outbox table ? publish to message broker ? mark as processed
```
Here we replace the DB table with an in-memory `Channel<T>` to focus on the
`IHostedService` + `Channel<T>` mechanics without introducing an actual broker.

---

## Endpoints

| Method | Route | Notes |
|--------|-------|-------|
| `POST` | `/outbox` | Writes message to `Channel<T>` ? 202 Accepted |
| `GET` | `/outbox/processed` | Returns messages already consumed by `OutboxConsumerService` |

---

## Project Structure (new / changed files)

```
Lesson/
  Messaging/
    OutboxMessage.cs          NEW  Record — channel message payload
    OutboxChannel.cs          NEW  Wraps Channel<OutboxMessage>; exposes Writer/Reader
  HostedServices/
    OutboxConsumerService.cs  NEW  BackgroundService — drains channel, simulates processing
  Controllers/
    OutboxController.cs       NEW  /outbox producer endpoints
  Program.cs                        + singleton channel, consumer, hosted service
Lesson.Tests/
  ChannelHostedServiceTests.cs  NEW  6 integration tests (with polling helper for async)
```

---

## Tests

```bash
dotnet test --filter "FullyQualifiedName~ChannelHostedServiceTests"
# 6 tests — all pass
```

| Test | What it verifies |
|---|---|
| `Publish_Returns202Accepted` | Producer endpoint returns 202 |
| `Publish_ResponseContainsMessageId` | Guid and queued flag present |
| `AfterPublish_MessageIsProcessedByBackgroundService` | Consumer receives the message |
| `AfterMultiplePublishes_AllMessagesProcessed` | All messages consumed in order |
| `ProcessedEntry_ContainsCorrectEventType` | Data integrity through the queue |
| `ProcessedEntry_PayloadIsSerializedJson` | Payload serialised as JSON |

> **Testing note:** Because the consumer is asynchronous, tests use a `WaitForProcessedAsync`
> polling helper that retries the `/outbox/processed` endpoint up to 2 seconds before failing.
> In production code, prefer awaitable completion signals over polling.

---

## Exercises

1. Switch from `UnboundedChannel` to `BoundedChannel` with capacity 3 and observe what happens when you publish 4 messages rapidly with `BoundedChannelFullMode.Wait`.
2. Add a `DeadLetterChannel` — if processing throws, write the failed message to a second `Channel<T>` and expose a `/outbox/dead-letter` endpoint.
3. Add a second `BackgroundService` that reads from the same channel — observe that with `SingleReader = true` you get a runtime exception, then switch to `SingleReader = false`.
4. Introduce a simulated transactional outbox: save the `OutboxMessage` to the SQLite `BankingDbContext` in the controller, and have the background service query the DB instead of reading from the channel.


> **Branch:** `lesson/08-events/b-intermediate`
> **Prerequisites:** Lesson 08-A (delegates, event keyword, in-process pub/sub)

---

## What you will learn

| Topic | C# / MediatR | Java parallel |
|---|---|---|
| `INotification` | marker interface for a domain event | `ApplicationEvent` subclass |
| `INotificationHandler<T>` | receives a notification; multiple per notification | `@EventListener` method |
| `IMediator.Publish()` | dispatch to all handlers | `ApplicationEventPublisher.publishEvent()` |
| Decoupling | publisher has zero dependency on handlers | `@EventListener` — no direct coupling either |
| Multiple handlers | fan-out — all handlers are called | Multiple `@EventListener` methods for the same event type |

---

## 1. INotification — the event payload

```csharp
public record AccountCreatedNotification(
    Guid AccountId,
    string OwnerName,
    decimal InitialBalance) : INotification;
```

`INotification` is a **marker interface** — no methods required.
MediatR uses the type to route the notification to the correct handlers.

**Java parallel:**
```java
public class AccountCreatedEvent extends ApplicationEvent {
    public AccountCreatedEvent(Object source, UUID accountId, String ownerName) { ... }
}
```

---

## 2. INotificationHandler — subscribers

```csharp
public class SendWelcomeEmailHandler : INotificationHandler<AccountCreatedNotification>
{
    public Task Handle(AccountCreatedNotification notification, CancellationToken ct)
    {
        // send welcome email (simulated)
        return Task.CompletedTask;
    }
}

public class AccountCreatedAuditHandler : INotificationHandler<AccountCreatedNotification>
{
    public Task Handle(AccountCreatedNotification notification, CancellationToken ct)
    {
        _log.Add(notification); // record in audit
        return Task.CompletedTask;
    }
}
```

Both handlers are called automatically when the notification is published.
Handlers are registered by `AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<Program>())`.

**Java parallel:**
```java
@Component class WelcomeEmailListener {
    @EventListener AccountCreatedEvent handle(AccountCreatedEvent e) { ... }
}
@Component class AuditListener {
    @EventListener AccountCreatedEvent handle(AccountCreatedEvent e) { ... }
}
```

---

## 3. Publishing via IMediator

```csharp
[ApiController] [Route("accounts-events")]
public class AccountEventsController(IMediator mediator) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateAccountRequest request, CancellationToken ct)
    {
        var notification = new AccountCreatedNotification(Guid.NewGuid(), request.OwnerName, request.InitialBalance);
        await mediator.Publish(notification, ct);  // fan-out to all handlers
        return Ok(new { notification.AccountId, notification.OwnerName });
    }
}
```

The controller knows about `IMediator` but has **no knowledge** of `SendWelcomeEmailHandler`
or `AccountCreatedAuditHandler` — fully decoupled.

---

## INotification vs C# event keyword

| | C# `event` (Lesson 08-A) | MediatR `INotification` (Lesson 08-B) |
|---|---|---|
| Coupling | subscriber must reference the publisher to `+=` | zero coupling — handlers registered via DI |
| Discovery | compile-time | DI scan via `AddMediatR` |
| Async | requires `async void` (discouraged) | `async Task` natively supported |
| Testing | must wire event manually | inject mock `IMediator` |

---

## Endpoints

| Method | Route | Notes |
|--------|-------|-------|
| `POST` | `/accounts-events` | Publishes `AccountCreatedNotification` to all handlers |
| `GET` | `/accounts-events/audit` | Returns audit log populated by `AccountCreatedAuditHandler` |
| `DELETE` | `/accounts-events/audit/reset` | Test helper |

---

## Project Structure (new / changed files)

```
Lesson/
  Notifications/
    AccountCreatedNotification.cs   NEW  INotification record
  Handlers/
    AccountNotificationHandlers.cs  NEW  SendWelcomeEmailHandler + AccountCreatedAuditHandler
  Controllers/
    AccountEventsController.cs      NEW  /accounts-events/* endpoints
Lesson.Tests/
  MediatRNotificationTests.cs       NEW  7 integration tests
```

---

## Tests

```bash
dotnet test --filter "FullyQualifiedName~MediatRNotificationTests"
# 7 tests — all pass
```

| Test | What it verifies |
|---|---|
| `Create_Returns200AndAccountId` | Publish succeeds; Guid returned |
| `Create_OwnerNamePreservedInResponse` | Data flows through notification |
| `AfterCreate_AuditHandlerReceivesNotification` | Fan-out — audit handler called |
| `AfterCreate_AuditEntryHasCorrectBalance` | Data integrity through the pipeline |
| `MultipleCreates_AllRecordedInAuditLog` | Multiple notifications accumulated |
| `ResetAudit_ClearsLog` | Test helper works |
| `Create_WithZeroBalance_StillPublishesAndRecords` | No validation barrier at this level |

---

## Exercises

1. Add a `TransactionCompletedNotification` with a third handler that sends an SMS (simulated) — verify all three handlers run with a single `Publish` call.
2. Throw an exception inside `SendWelcomeEmailHandler` and observe MediatR's default behaviour (it propagates by default). Then wrap it with a try/catch inside the handler.
3. Add `[Transactional]`-style behaviour: use `IPipelineBehavior` to wrap `Publish` in a using block that logs start/end.
4. Replace `IMediator.Publish` with `IMediator.Send` (returning a value) and compare the semantics — `Send` expects exactly one handler; `Publish` allows zero or many.


> **Branch:** `lesson/08-events/a-basic`
> **Prerequisites:** Lesson 07-C (custom exception hierarchy, MediatR)

---

## What you will learn

| Topic | C# | Java parallel |
|---|---|---|
| `delegate` | type-safe multicast function pointer | `java.util.function.*` / custom `@FunctionalInterface` |
| `event` keyword | restricts delegate — external code can only `+=` / `-=` | `ApplicationEventPublisher` / custom listener pattern |
| `EventHandler<T>` | standard `(object? sender, T args)` signature | `ApplicationListener<E>` |
| `EventArgs` subclass | typed event payload | `ApplicationEvent` subclass |
| `Action<T>` | delegate taking a parameter, returning void | `Consumer<T>` |
| `Func<T, TResult>` | delegate taking a parameter, returning a value | `Function<T, R>` |
| Multicast delegate | `handler += subscriber` — all subscribers called | `List<ApplicationListener>` |
| In-process event bus | singleton class that holds and fires the event | `@Component` + `ApplicationEventPublisher` |

---

## 1. C# Delegates

A delegate is a **type that holds a reference to a method** (or multiple methods — multicast).

```csharp
// Action<T> — void return
Action<string> log = msg => Console.WriteLine(msg);
log("hello");

// Func<T, TResult> — non-void return
Func<decimal, decimal> tax = amount => amount * 1.2m;
var total = tax(100m); // 120

// Multicast — += adds a subscriber
Action<string> multi = s => { };
multi += s => Console.WriteLine($"[A] {s}");
multi += s => Console.WriteLine($"[B] {s}");
multi("fired"); // both A and B are called
```

**Java parallel:** `Consumer<String>` for `Action<T>`, `Function<BigDecimal,BigDecimal>` for `Func<T,R>`.

---

## 2. event keyword

`event` wraps a delegate and enforces encapsulation:
- **Inside** the class: can invoke (raise) the event with `?.Invoke(...)`.
- **Outside** the class: can only subscribe (`+=`) or unsubscribe (`-=`).

```csharp
public class DomainEventBus
{
    public event EventHandler<PaymentCreatedEventArgs>? PaymentCreated;

    public void PublishPaymentCreated(PaymentCreatedEventArgs args) =>
        PaymentCreated?.Invoke(this, args);   // null-safe raise
}
```

**Java parallel:**
```java
@Component
public class DomainEventBus {
    @Autowired ApplicationEventPublisher publisher;

    public void publishPaymentCreated(PaymentCreatedEvent event) {
        publisher.publishEvent(event);
    }
}
```

---

## 3. Subscriber

```csharp
public class PaymentAuditSubscriber
{
    public PaymentAuditSubscriber(DomainEventBus bus)
    {
        bus.PaymentCreated += OnPaymentCreated;   // subscribe
    }

    private void OnPaymentCreated(object? sender, PaymentCreatedEventArgs args) =>
        _log.Add(args);
}
```

**Java parallel:**
```java
@Component
public class PaymentAuditListener {
    @EventListener
    public void onPaymentCreated(PaymentCreatedEvent event) { ... }
}
```

---

## 4. Registration in Program.cs

```csharp
// Both are Singleton — the subscriber is created eagerly to connect the event wire
builder.Services.AddSingleton<DomainEventBus>();
builder.Services.AddSingleton<PaymentAuditSubscriber>();
```

The ASP.NET Core DI container resolves `PaymentAuditSubscriber` on first use (lazy Singleton).
The constructor subscribes to `DomainEventBus.PaymentCreated` at that point.

---

## Endpoints

| Method | Route | Notes |
|--------|-------|-------|
| `POST` | `/event-demo/payment` | Publishes `PaymentCreated` event |
| `GET` | `/event-demo/audit` | Returns in-memory audit log |
| `GET` | `/event-demo/delegate-demo` | Illustrates `Action<T>`, `Func<T,R>`, multicast |

---

## Project Structure (new / changed files)

```
Lesson/
  Events/
    DomainEventBus.cs           NEW  event keyword, PaymentCreatedEventArgs
  Subscribers/
    PaymentAuditSubscriber.cs   NEW  subscribes via +=, maintains audit log
  Controllers/
    EventDemoController.cs      NEW  /event-demo/* endpoints
  Program.cs                          + AddSingleton for bus and subscriber
Lesson.Tests/
  EventBasicTests.cs            NEW  7 integration tests
```

---

## Tests

```bash
dotnet test --filter "FullyQualifiedName~EventBasicTests"
# 7 tests — all pass
```

| Test | What it verifies |
|---|---|
| `PublishPayment_Returns200` | Event publish succeeds |
| `PublishPayment_ResponseContainsPaymentId` | Guid returned |
| `AfterPublish_AuditLogContainsEntry` | Subscriber received the event |
| `MultiplePublishes_AllRecordedInAuditLog` | Multicast — all events recorded |
| `AuditEntry_ContainsCorrectAmount` | Event data is correct |
| `DelegateDemo_Returns200` | Delegate demo endpoint works |
| `DelegateDemo_FuncResultIs120` | `Func<decimal,decimal>` applies 20% tax correctly |

---

## Exercises

1. Add a second subscriber `PaymentEmailSubscriber` that accumulates `(fromAccount, toAccount)` pairs and expose a `/event-demo/emails` endpoint to verify it.
2. Unsubscribe from the event (`-=`) inside `PaymentAuditSubscriber` after recording 3 events and verify the log stops growing.
3. Change `OnPaymentCreated` to be an async method and observe the compilation error — then read about why `async void` event handlers are generally discouraged.
4. Add `Predicate<T>` (another built-in delegate) to the delegate demo: filter payments above a threshold amount.


> **Branch:** `lesson/07-error-handling/c-advanced`
> **Prerequisites:** Lesson 07-B (IExceptionHandler, ProblemDetails, FluentValidation)

---

## What you will learn

| Topic | C# ASP.NET Core | Java parallel |
|---|---|---|
| Custom exception hierarchy | `DomainException` base ? typed subclasses | `RuntimeException` hierarchy + `@ResponseStatus` |
| `NotFoundException` ? 404 | thrown from handler, caught globally | `ResponseStatusException(NOT_FOUND)` |
| `BusinessRuleException` ? 422 | domain rule violation | `ResponseStatusException(UNPROCESSABLE_ENTITY)` |
| Multiple `IExceptionHandler` | **registration order = evaluation order** | `@ExceptionHandler` specificity in `@ControllerAdvice` |
| `IPipelineBehavior<TReq, TRes>` | MediatR middleware (validation, logging, etc.) | Spring AOP `@Around` advice |
| `ValidationBehavior<T>` | runs FluentValidation before every handler | `@Validated` service + `MethodValidationInterceptor` |

---

## 1. Custom Exception Hierarchy

```csharp
public abstract class DomainException(string message, int statusCode = 400) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

public class NotFoundException(string resource, object key)
    : DomainException($"{resource} '{key}' was not found.", 404);

public class BusinessRuleException(string message)
    : DomainException(message, 422);

public class ForbiddenException(string message)
    : DomainException(message, 403);
```

Exceptions carry their own HTTP status — the handler needs no `if/else` chain.

**Java parallel:**
```java
@ResponseStatus(HttpStatus.NOT_FOUND)
public class NotFoundException extends RuntimeException { ... }
```

---

## 2. DomainExceptionHandler + Handler Registration Order

```csharp
// Specific handlers FIRST — GlobalExceptionHandler is the catch-all at the end
builder.Services.AddExceptionHandler<DomainExceptionHandler>();
builder.Services.AddExceptionHandler<ValidationExceptionHandler>();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
```

`DomainExceptionHandler` returns `false` if the exception is not a `DomainException`,
passing control to the next registered handler.

**Java parallel:** `@ExceptionHandler` methods are matched by most-specific type first;
`@ExceptionHandler(Exception.class)` is the catch-all.

---

## 3. MediatR IPipelineBehavior — Validation Middleware

```csharp
public class ValidationBehavior<TRequest, TResponse>(
    IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken ct)
    {
        var failures = (await Task.WhenAll(
                validators.Select(v => v.ValidateAsync(request, ct))))
            .SelectMany(r => r.Errors)
            .Where(f => f is not null)
            .ToList();

        if (failures.Count > 0)
            throw new ValidationException(failures); // caught by ValidationExceptionHandler

        return await next();
    }
}
```

**Registered once** — applies to ALL MediatR requests:
```csharp
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
```

**Java parallel:** Spring AOP `@Around` `@Validated` service interceptor.

---

## 4. Command + Handler pattern

```csharp
public record CreatePaymentCommand(string FromAccount, string ToAccount, decimal Amount)
    : IRequest<PaymentResult>;

public class CreatePaymentHandler : IRequestHandler<CreatePaymentCommand, PaymentResult>
{
    public Task<PaymentResult> Handle(CreatePaymentCommand request, CancellationToken ct)
    {
        if (_blocked.Contains(request.FromAccount))
            throw new BusinessRuleException($"Account {request.FromAccount} is blocked.");

        return Task.FromResult(new PaymentResult(Guid.NewGuid(), ...));
    }
}
```

**Java parallel:** Spring `@Service` method called by the controller — MediatR decouples the
controller from the handler via message-passing.

---

## Exception Handler Pipeline (all three parts combined)

```
Unhandled exception
  ? DomainExceptionHandler    (returns true for DomainException subtypes)
  ? ValidationExceptionHandler (returns true for FluentValidation.ValidationException)
  ? GlobalExceptionHandler    (catch-all ? 500 ProblemDetails)
```

---

## Endpoints

| Method | Route | Notes |
|--------|-------|-------|
| `POST` | `/payments` | MediatR pipeline ? validation ? handler |
| `GET` | `/payments/{id}` | Always throws NotFoundException ? 404 |
| `GET` | `/payments/forbidden` | Throws ForbiddenException ? 403 |

---

## Project Structure (new / changed files)

```
Lesson/
  Exceptions/
    DomainException.cs             NEW  DomainException + NotFoundException + BusinessRuleException + ForbiddenException
  ExceptionHandlers/
    DomainExceptionHandler.cs      NEW  Maps DomainException ? ProblemDetails by StatusCode
    ValidationExceptionHandler.cs  NEW  Maps FluentValidation.ValidationException ? 400
  Pipeline/
    ValidationBehavior.cs          NEW  IPipelineBehavior — runs validators before handler
  Commands/
    CreatePaymentCommand.cs        NEW  IRequest + validator + IRequestHandler
  Controllers/
    PaymentsController.cs          NEW  /payments/* endpoints
  Program.cs                              + ordered exception handler registration + MediatR
Lesson.Tests/
  ErrorHandlingAdvancedTests.cs    NEW  9 integration tests
```

---

## Tests

```bash
dotnet test --filter "FullyQualifiedName~ErrorHandlingAdvancedTests"
# 9 tests — all pass
```

| Test | What it verifies |
|---|---|
| `Get_PaymentNotFound_Returns404` | NotFoundException ? 404 |
| `Get_NotFound_ResponseIsProblemDetails` | ProblemDetails status field |
| `Forbidden_Returns403` | ForbiddenException ? 403 |
| `Forbidden_ResponseIsProblemDetails` | ProblemDetails for 403 |
| `Create_WhenAmountZero_Returns400ViaMediatRPipeline` | ValidationBehavior catches invalid command |
| `Create_WhenSameAccount_Returns400WithValidationError` | Cross-property rule in MediatR pipeline |
| `Create_WhenAccountBlocked_Returns422BusinessRule` | BusinessRuleException from handler ? 422 |
| `Create_WithValidCommand_Returns201` | Happy path |
| `Create_WithValidCommand_ResponseContainsPaymentId` | Response body has Guid |

---

## Exercises

1. Add a `ConflictException` (409) and throw it when the same `FromAccount + ToAccount + Amount` combination is submitted twice.
2. Add a `LoggingBehavior<TRequest, TResponse>` that logs the command type, execution time, and whether it succeeded or threw.
3. Create a second `IRequestHandler` with its own `AbstractValidator` and verify the `ValidationBehavior` runs the correct validator per command type.
4. Move `ValidationExceptionHandler` after `GlobalExceptionHandler` and observe which tests fail — then restore the correct order.


> **Branch:** `lesson/07-error-handling/b-intermediate`
> **Prerequisites:** Lesson 07-A (try/catch, Data Annotations, ModelState)

---

## What you will learn

| Topic | C# ASP.NET Core | Java parallel |
|---|---|---|
| `IExceptionHandler` (.NET 8+) | global unhandled exception handler | `@ControllerAdvice` + `@ExceptionHandler(Exception.class)` |
| `ProblemDetails` (RFC 7807) | standardised JSON error format | Spring 6 `ProblemDetail` |
| `AddExceptionHandler<T>()` | DI registration of the handler | `@Bean ExceptionResolver` |
| `FluentValidation` | rule-based validation in a separate class | Hibernate Validator `ConstraintValidator<A,T>` |
| Cross-property rules | `.NotEqual(x => x.From)` | `@ScriptAssert` / custom `@Constraint` |

---

## 1. IExceptionHandler — Global Error Handling

```csharp
public class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken ct)
    {
        logger.LogError(exception, "Unhandled exception: {Message}", exception.Message);

        var problem = new ProblemDetails
        {
            Status = 500,
            Title  = "An unexpected error occurred.",
            Detail = exception.Message,
            Type   = "https://tools.ietf.org/html/rfc9110#section-15.6.1"
        };
        httpContext.Response.StatusCode = 500;
        await httpContext.Response.WriteAsJsonAsync(problem, ct);

        return true; // stop handler chain
    }
}
```

**Registration:**
```csharp
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();
// ...
app.UseExceptionHandler();
```

**Java parallel:**
```java
@ControllerAdvice
public class GlobalHandler {
    @ExceptionHandler(Exception.class)
    public ResponseEntity<ProblemDetail> handle(Exception ex) {
        var pd = ProblemDetail.forStatusAndDetail(HttpStatus.INTERNAL_SERVER_ERROR, ex.getMessage());
        return ResponseEntity.of(pd).build();
    }
}
```

---

## 2. ProblemDetails (RFC 7807) shape

```json
{
  "type":    "https://tools.ietf.org/html/rfc9110#section-15.6.1",
  "title":   "An unexpected error occurred.",
  "status":  500,
  "detail":  "This was not caught by the action.",
  "traceId": "00-abc123..."
}
```

`[ApiController]` automatically returns `ValidationProblemDetails` (extends `ProblemDetails`)
for model validation failures — same format, status 400 with an `errors` dictionary.

---

## 3. FluentValidation

```csharp
public class CreateTransferRequestValidator : AbstractValidator<CreateTransferRequest>
{
    public CreateTransferRequestValidator()
    {
        RuleFor(x => x.FromAccount)
            .NotEmpty()
            .Length(5, 20);

        RuleFor(x => x.ToAccount)
            .NotEmpty()
            .Length(5, 20)
            .NotEqual(x => x.FromAccount)   // cross-property rule
            .WithMessage("Source and destination accounts must differ.");

        RuleFor(x => x.Amount)
            .InclusiveBetween(0.01m, 1_000_000m);
    }
}
```

**Registered via assembly scan:**
```csharp
builder.Services.AddValidatorsFromAssemblyContaining<CreateTransferRequestValidator>();
```

**Used manually in a controller** (Lesson 07-C shows pipeline integration with MediatR):
```csharp
var result = await validator.ValidateAsync(request, ct);
if (!result.IsValid)
    return ValidationProblem(new ValidationProblemDetails(...));
```

**Java parallel:** `@Autowired Validator validator; validator.validate(request)` — same manual
approach before integrating with `@Valid`.

---

## Endpoints

| Method | Route | Notes |
|--------|-------|-------|
| `GET` | `/error-demo/unhandled` | Throws unhandled ? `GlobalExceptionHandler` ? 500 ProblemDetails |
| `POST` | `/error-demo/fluent-validate` | Validates via FluentValidation; 400 or 200 |

---

## Project Structure (new / changed files)

```
Lesson/
  ExceptionHandlers/
    GlobalExceptionHandler.cs    NEW  IExceptionHandler ? ProblemDetails 500
  Validators/
    CreateTransferRequestValidator.cs  NEW  FluentValidation rules
  Controllers/
    ErrorDemoController.cs       NEW  /error-demo/* endpoints
  Program.cs                           + AddExceptionHandler, AddProblemDetails,
                                         AddValidatorsFromAssemblyContaining,
                                         app.UseExceptionHandler()
Lesson.Tests/
  ErrorHandlingIntermediateTests.cs  NEW  9 integration tests
```

---

## Tests

```bash
dotnet test --filter "FullyQualifiedName~ErrorHandlingIntermediateTests"
# 9 tests — all pass
```

| Test | What it verifies |
|---|---|
| `UnhandledException_Returns500` | Global handler catches unhandled exception |
| `UnhandledException_ResponseIsProblemDetails` | Status field = 500 |
| `UnhandledException_ProblemDetails_HasTitle` | Title is present |
| `UnhandledException_ProblemDetails_DetailContainsExceptionMessage` | Detail contains exception message |
| `FluentValidate_WhenAmountTooLow_Returns400` | Range rule violation |
| `FluentValidate_WhenSameAccount_Returns400WithCrossPropertyError` | Cross-property rule |
| `FluentValidate_WhenAccountTooShort_Returns400` | Length rule violation |
| `FluentValidate_WithValidPayload_Returns200` | Happy path |
| `FluentValidate_WithValidPayload_ResponseContainsAmount` | Response body correct |

---

## Exercises

1. Register a second `IExceptionHandler` that specifically handles `ArgumentException` with 422 Unprocessable Entity — handlers are called in registration order.
2. Add a `MustAsync` rule to `CreateTransferRequestValidator` that checks account numbers are not on a "blocked list" (simulate with a list in memory).
3. Replace the manual `validator.ValidateAsync(...)` in `ErrorDemoController` with a global `IActionFilter` that runs FluentValidation automatically for all POST/PUT requests.
4. Add an `extensions` field to the `ProblemDetails` (e.g. `correlationId`) pulled from `HttpContext.Items`.


> **Branch:** `lesson/07-error-handling/a-basic`
> **Prerequisites:** Lesson 06-C (advanced filters)

---

## What you will learn

| Topic | C# ASP.NET Core | Java parallel |
|---|---|---|
| `try/catch` in controllers | catch ? map to `IActionResult` | `@ExceptionHandler` in controller |
| `[ApiController]` auto-validation | returns 400 `ValidationProblemDetails` automatically | `@Valid` + `MethodArgumentNotValidException` |
| Data Annotations | `[Required]`, `[Range]`, `[StringLength]`, `[MaxLength]` | Bean Validation: `@NotNull`, `@Size`, `@Min`, `@Max` |
| `ModelState` | dictionary of field ? error list | `BindingResult` |
| HTTP error responses | `BadRequest()`, `NotFound()`, `StatusCode(500,...)` | `ResponseEntity<>` |

---

## 1. Data Annotations

Attributes placed on model properties declare validation rules.
ASP.NET Core evaluates them automatically during model binding.

```csharp
public class CreateTransferRequest
{
    [Required(ErrorMessage = "Source account number is required.")]
    [StringLength(20, MinimumLength = 5)]
    public string FromAccount { get; set; } = string.Empty;

    [Range(0.01, 1_000_000)]
    public decimal Amount { get; set; }

    [MaxLength(200)]
    public string? Description { get; set; }
}
```

**Java parallel:**
```java
public record CreateTransferRequest(
    @NotBlank @Size(min=5, max=20) String fromAccount,
    @DecimalMin("0.01") @DecimalMax("1000000") BigDecimal amount,
    @Size(max=200) String description
) {}
```

---

## 2. [ApiController] automatic validation

When a controller is decorated with `[ApiController]`, ASP.NET Core checks `ModelState`
before the action method runs. If validation fails it returns a `400 ValidationProblemDetails`
**without any extra code in the action**.

```csharp
[ApiController]
[Route("transfers")]
public class TransferController : ControllerBase
{
    [HttpPost]
    public IActionResult Create([FromBody] CreateTransferRequest request)
    {
        // If annotations were violated, this line is never reached.
        // ...
    }
}
```

**Java parallel:** Spring MVC's `DefaultHandlerExceptionResolver` converts
`MethodArgumentNotValidException` to 400 when `@Valid` is used.

---

## 3. try/catch ? IActionResult

```csharp
try
{
    if (request.FromAccount == request.ToAccount)
        return BadRequest(new { error = "Source and destination accounts must differ." });

    // ... business logic
}
catch (Exception ex)
{
    return StatusCode(500, new { error = "Unexpected error.", detail = ex.Message });
}
```

Lesson 07-B replaces per-action try/catch with a global exception handler.

---

## Endpoints

| Method | Route | Notes |
|--------|-------|-------|
| `POST` | `/transfers` | Validated by annotations; 201 on success, 400 on violation |
| `GET` | `/transfers/{id}` | 404 if not found |
| `DELETE` | `/transfers/reset` | Test helper — clears in-memory list |
| `GET` | `/transfers/simulate-error` | Forces a caught exception ? 500 |

---

## Project Structure (new / changed files)

```
Lesson/
  Models/
    CreateTransferRequest.cs   NEW  Data Annotations validation model
  Controllers/
    TransferController.cs      NEW  try/catch, ModelState, IActionResult errors
Lesson.Tests/
  ErrorHandlingBasicTests.cs   NEW  9 integration tests
```

---

## Tests

```bash
dotnet test --filter "FullyQualifiedName~ErrorHandlingBasicTests"
# 9 tests — all pass
```

| Test | What it verifies |
|---|---|
| `Create_WhenAmountIsZero_Returns400` | `[Range]` violation ? 400 |
| `Create_WhenFromAccountMissing_Returns400WithErrors` | `[Required]` ? 400 with error dictionary |
| `Create_WhenAccountNumberTooShort_Returns400` | `[StringLength]` min ? 400 |
| `Create_WhenDescriptionTooLong_Returns400` | `[MaxLength]` ? 400 |
| `Create_WhenSameAccount_Returns400WithDomainError` | Domain rule ? 400 |
| `Create_WithValidPayload_Returns201` | Happy path ? 201 |
| `GetById_WhenNotFound_Returns404` | Not found ? 404 |
| `GetById_AfterCreate_Returns200` | Persisted correctly ? 200 |
| `SimulateError_Returns500WithMessage` | try/catch ? 500 with message |

---

## Exercises

1. Add a custom `[FutureDate]` attribute that validates a `DateTime` is in the future.
2. Add a `[FromQuery]` parameter with `[Range]` to one of the GET endpoints and verify validation applies.
3. Disable `[ApiController]`'s automatic validation suppression and add an explicit `if (!ModelState.IsValid) return ValidationProblem(ModelState)` — verify the same tests pass.
4. Add a second domain rule: `Amount` must be divisible by `0.01` (i.e. at most 2 decimal places).


> **Branch:** `lesson/06-middleware/c-advanced`
> **Prerequisites:** Lesson 06-B (IActionFilter, IAsyncActionFilter)

---

## What you will learn

| Topic | C# ASP.NET Core | Java parallel |
|---|---|---|
| `IResourceFilter` | wraps model binding; can cache / short-circuit early | Servlet Filter checking ETags |
| `IResultFilter` | wraps result execution; can transform the response body | `ResponseBodyAdvice<T>.beforeBodyWrite()` |
| `IEndpointFilter` | minimal-API equivalent of `IActionFilter` (.NET 7+) | `HandlerInterceptor` on a specific path |
| `[TypeFilter(typeof(T))]` | apply a filter per-action with DI support | — |
| `context.Result = ...` | short-circuit (resource filter) | `filterChain.doFilter()` not called |

---

## 1. Filter Execution Order

```
Request
  ? [Middleware]
    ? IResourceFilter.OnResourceExecuting   ? can short-circuit (cache hit)
      ? Model binding
        ? IActionFilter.OnActionExecuting
          ? Action method
        ? IActionFilter.OnActionExecuted
      ? IResultFilter.OnResultExecuting      ? can transform the result
        ? Write response body
      ? IResultFilter.OnResultExecuted
    ? IResourceFilter.OnResourceExecuted     ? response written; can log/audit
```

Use `IResourceFilter` when you need to act **before model binding** (e.g. caching).
Use `IResultFilter` when you need to **transform the serialized response**.

---

## 2. ResponseCacheFilter (IResourceFilter)

```csharp
public class ResponseCacheFilter : IResourceFilter
{
    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        if (_cache.TryGetValue(CacheKey(context.HttpContext), out var cached))
            context.Result = cached; // short-circuit — action never runs
    }

    public void OnResourceExecuted(ResourceExecutedContext context)
    {
        if (context.Result is ObjectResult result)
            _cache[CacheKey(context.HttpContext)] = result; // populate cache
    }
}
```

On a cache hit the action is **never invoked** — not even model binding runs.

---

## 3. EnvelopeResultFilter (IResultFilter)

```csharp
public class EnvelopeResultFilter : IResultFilter
{
    public void OnResultExecuting(ResultExecutingContext context)
    {
        if (context.Result is ObjectResult { StatusCode: null or >= 200 and < 300 } r)
            context.Result = new ObjectResult(new
            {
                data = r.Value,
                meta = new { timestamp = DateTime.UtcNow, version = "06-C" }
            });
    }

    public void OnResultExecuted(ResultExecutedContext context) { }
}
```

**Java parallel:** `ResponseBodyAdvice<T>` — modify the return value before Jackson serialises it.

---

## 4. ApiKeyEndpointFilter (IEndpointFilter — minimal API)

```csharp
public class ApiKeyEndpointFilter(string requiredKey) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var provided = context.HttpContext.Request.Query["apiKey"].ToString();
        if (provided != requiredKey)
            return Results.Unauthorized(); // short-circuit

        return await next(context);
    }
}
```

**Registered on a minimal-API route:**
```csharp
app.MapGet("/minimal/secure", () => Results.Ok(new { secret = "you have the key!" }))
   .AddEndpointFilter(new ApiKeyEndpointFilter("lesson06"));
```

---

## Endpoints

| Method | Route | Notes |
|--------|-------|-------|
| `GET` | `/advanced-filters/cached` | Served from cache on second call |
| `DELETE` | `/advanced-filters/reset-cache` | Clears the in-process cache |
| `GET` | `/advanced-filters/envelope` | Result wrapped in `{ data, meta }` |
| `GET` | `/minimal/secure?apiKey=lesson06` | Protected by `ApiKeyEndpointFilter` |

---

## Project Structure (new / changed files)

```
Lesson/
  Filters/
    ResponseCacheFilter.cs      NEW  IResourceFilter — in-process response cache
    EnvelopeResultFilter.cs     NEW  IResultFilter — { data, meta } envelope
    ApiKeyEndpointFilter.cs     NEW  IEndpointFilter — apiKey query-param guard
  Controllers/
    AdvancedFilterController.cs NEW  /advanced-filters/* endpoints
  Program.cs                          + minimal-API /minimal/secure route
Lesson.Tests/
  AdvancedFilterTests.cs        NEW  9 integration tests
```

---

## Tests

```bash
dotnet test --filter "FullyQualifiedName~AdvancedFilterTests"
# 9 tests — all pass
```

| Test | What it verifies |
|---|---|
| `Cached_FirstCall_Returns200` | Cache miss — action runs, 200 returned |
| `Cached_SecondCall_ReturnsCachedResult` | Cache hit — `callCount` stays at 1 |
| `Envelope_Returns200` | Filter is transparent to status code |
| `Envelope_ResponseBodyContainsDataAndMeta` | Result wrapped in `{ data, meta }` |
| `Envelope_MetaContainsVersion` | Meta contains `version: "06-C"` |
| `SecureEndpoint_WithoutApiKey_Returns401` | Missing key ? 401 |
| `SecureEndpoint_WithWrongApiKey_Returns401` | Wrong key ? 401 |
| `SecureEndpoint_WithCorrectApiKey_Returns200` | Correct key ? 200 |
| `SecureEndpoint_WithCorrectApiKey_ReturnsSecret` | Body contains secret value |

---

## Exercises

1. Extend `ResponseCacheFilter` to honour a `?bust=1` query parameter that forces a cache bypass and refresh.
2. Add an `X-Cache: HIT` / `X-Cache: MISS` response header inside `ResponseCacheFilter`.
3. Convert `ApiKeyEndpointFilter` to read the key from `IConfiguration` instead of a constructor argument.
4. Apply `EnvelopeResultFilter` **globally** and observe which existing tests would need updating.


> **Branch:** `lesson/06-middleware/b-intermediate`
> **Prerequisites:** Lesson 06-A (custom middleware, IMiddleware)

---

## What you will learn

| Topic | C# ASP.NET Core | Java parallel |
|---|---|---|
| `IActionFilter` | sync filter (OnActionExecuting / OnActionExecuted) | `HandlerInterceptor` (`preHandle` / `postHandle`) |
| `IAsyncActionFilter` | async filter with `ActionExecutionDelegate next` | `HandlerInterceptor` (async variant) |
| Global registration | `AddControllers(o => o.Filters.Add<T>())` | `WebMvcConfigurer.addInterceptors()` |
| Per-action attribute | `[TypeFilter(typeof(T))]` | `@Annotation` on method |
| Correlation ID | read from request header, store in `HttpContext.Items`, echo back | `ThreadLocal` + `preHandle` / `postHandle` |
| Short-circuiting | set `context.Result` without calling `next()` | `preHandle()` returning `false` |

---

## 1. IActionFilter vs IMiddleware

| | `IMiddleware` | `IActionFilter` |
|---|---|---|
| Scope | Every request (incl. static files, unknown routes) | Only requests that reach a controller action |
| Access to action metadata | ? | ? (`ActionDescriptor`, `ActionArguments`) |
| Short-circuit | Set `context.Result` in `OnResultExecuting` | Set `context.Result` in `OnActionExecuting` |
| Async | Implement `InvokeAsync` | Implement `IAsyncActionFilter` |

Use **middleware** for cross-cutting concerns that apply to all requests (logging, headers).  
Use **action filters** when you need access to MVC-specific context (model binding, action parameters).

---

## 2. CorrelationIdFilter (global, IActionFilter)

```csharp
public class CorrelationIdFilter : IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext context)
    {
        var correlationId = context.HttpContext.Request.Headers["X-Correlation-Id"].FirstOrDefault()
                            ?? Guid.NewGuid().ToString();

        context.HttpContext.Items["CorrelationId"] = correlationId;          // store for logging
        context.HttpContext.Response.Headers["X-Correlation-Id"] = correlationId; // echo back
    }

    public void OnActionExecuted(ActionExecutedContext context) { }
}
```

**Registered globally:**
```csharp
builder.Services.AddControllers(options =>
    options.Filters.Add<CorrelationIdFilter>());
```

**Java parallel:**
```java
public class CorrelationInterceptor implements HandlerInterceptor {
    public boolean preHandle(HttpServletRequest request, HttpServletResponse response, Object handler) {
        String id = Optional.ofNullable(request.getHeader("X-Correlation-Id"))
                            .orElse(UUID.randomUUID().toString());
        response.setHeader("X-Correlation-Id", id);
        return true;
    }
}
```

---

## 3. RequireBodyFilter (per-action, IAsyncActionFilter)

```csharp
public class RequireBodyFilter : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(
        ActionExecutingContext context,
        ActionExecutionDelegate next)
    {
        var hasNullBodyParam = context.ActionDescriptor.Parameters
            .Any(p => p.BindingInfo?.BindingSource?.Id == "Body"
                      && context.ActionArguments.TryGetValue(p.Name, out var val)
                      && val is null);

        if (hasNullBodyParam)
        {
            context.Result = new BadRequestObjectResult(
                new { error = "Request body is required." });
            return; // short-circuit — action never runs
        }

        await next();
    }
}
```

**Applied per-action:**
```csharp
[HttpPost("body-required")]
[TypeFilter(typeof(RequireBodyFilter))]
public IActionResult BodyRequired([FromBody] SamplePayload? payload) => Ok(...);
```

**Java parallel:** `preHandle()` returning `false` — the controller method is never invoked.

---

## 4. Pipeline execution order

```
Request
  ? Middleware (ResponseHeaderMiddleware)
  ? Middleware (RequestLoggingMiddleware)
  ? MVC Router
    ? Filter: OnActionExecuting (CorrelationIdFilter)
    ? Filter: OnActionExecutionAsync (RequireBodyFilter) [if applied]
      ? Action Method
    ? Filter: OnActionExecuted (CorrelationIdFilter)
Response
```

---

## Endpoints

| Method | Route | Notes |
|--------|-------|-------|
| `GET` | `/filters/echo` | Returns correlation ID from `HttpContext.Items` |
| `GET` | `/filters/echo-header` | Echoes `X-Correlation-Id` from request header |
| `POST` | `/filters/body-required` | 400 if body missing (RequireBodyFilter), 200 otherwise |

---

## Project Structure (new / changed files)

```
Lesson/
  Filters/
    CorrelationIdFilter.cs   NEW  IActionFilter — global correlation ID
    RequireBodyFilter.cs     NEW  IAsyncActionFilter — short-circuit on null body
  Controllers/
    FilterDemoController.cs  NEW  /filters/* endpoints
  Program.cs                      + global CorrelationIdFilter registration
Lesson.Tests/
  ActionFilterTests.cs       NEW  8 integration tests
```

---

## Tests

```bash
dotnet test --filter "FullyQualifiedName~ActionFilterTests"
# 8 tests — all pass
```

| Test | What it verifies |
|---|---|
| `Echo_ResponseAlwaysContainsCorrelationIdHeader` | Header present on every response |
| `Echo_WhenNoCorrelationIdSent_ResponseContainsAutoGeneratedGuid` | Auto-generated when absent |
| `Echo_WhenCorrelationIdSent_SameIdIsEchoedBack` | Client ID is preserved |
| `AnyControllerEndpoint_AlsoReceivesCorrelationIdHeader` | Global filter applies everywhere |
| `Echo_ResponseBody_ContainsCorrelationId` | Value stored in `HttpContext.Items` |
| `BodyRequired_WhenBodyMissing_Returns400` | Short-circuit works |
| `BodyRequired_WhenBodyProvided_Returns200` | Happy path unaffected |
| `BodyRequired_WhenBodyProvided_ResponseContainsValue` | Action runs and returns body |

---

## Exercises

1. Convert `CorrelationIdFilter` to `IAsyncActionFilter` and verify the same tests pass.
2. Add an `ExecutionTimeFilter` that measures the time between `OnActionExecuting` and `OnActionExecuted` and writes it to an `X-Execution-Time-Ms` response header.
3. Create a `[RequireApiKey]` filter attribute that reads `X-Api-Key` from the request header and short-circuits with `401 Unauthorized` if it is absent or wrong.
4. Register `RequireBodyFilter` **globally** and observe which existing tests break — then restore per-action registration.


> **Branch:** `lesson/06-middleware/a-basic`
> **Prerequisites:** Lesson 05-C (Advanced LINQ)

---

## What you will learn

| Topic | C# ASP.NET Core | Java parallel |
|---|---|---|
| `IMiddleware` | preferred DI-managed middleware contract | `OncePerRequestFilter` |
| `RequestDelegate next` | call the next component in the pipeline | `filterChain.doFilter()` |
| Middleware ordering | registration order in `Program.cs` controls execution | `FilterRegistrationBean.setOrder()` |
| Request/response logging | log before/after `next(context)` | `CommonsRequestLoggingFilter` |
| Response header injection | add header before `next()` | `OncePerRequestFilter` — `response.setHeader()` |

---

## 1. The Middleware Pipeline

ASP.NET Core processes every HTTP request through a **pipeline** of middleware components.
Each component can:
- Run code **before** the next component (inbound)
- Call `await next(context)` to pass control forward
- Run code **after** the next component returns (outbound)

```
Request ?  [ResponseHeaderMiddleware] ? [RequestLoggingMiddleware] ? [Router] ? Controller
Response ?                           ?                            ?          ?
```

Registration order in `Program.cs` determines pipeline order.  
Middleware registered **first** wraps everything registered after it.

---

## 2. IMiddleware vs Convention-Based Middleware

| Approach | Lifetime | DI injection |
|---|---|---|
| `IMiddleware` | managed by DI container | full constructor injection ? |
| Convention-based (`Invoke(HttpContext)`) | instantiated once at startup | only singleton-safe services in constructor |

`IMiddleware` is the modern, recommended approach because it integrates cleanly with the DI container.

---

## 3. RequestLoggingMiddleware

```csharp
public class RequestLoggingMiddleware(ILogger<RequestLoggingMiddleware> logger) : IMiddleware
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var sw = Stopwatch.StartNew();
        logger.LogInformation("? {Method} {Path}", context.Request.Method, context.Request.Path);

        await next(context);   // pass to next middleware / endpoint

        sw.Stop();
        logger.LogInformation("? {Method} {Path} {StatusCode} ({Elapsed}ms)",
            context.Request.Method, context.Request.Path,
            context.Response.StatusCode, sw.ElapsedMilliseconds);
    }
}
```

**Java parallel:** `OncePerRequestFilter.doFilterInternal()` — call `filterChain.doFilter()`,
then log after it returns.

---

## 4. ResponseHeaderMiddleware

```csharp
public class ResponseHeaderMiddleware : IMiddleware
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        context.Response.Headers["X-Powered-By"] = "ASP.NET Core 10 Lesson 06";
        await next(context);
        // response body has already started streaming — do not write body here
    }
}
```

Headers **must** be set before `next()` is called (or before the response body starts writing).

---

## 5. Registration in Program.cs

```csharp
// Register as transient so DI manages the lifetime
builder.Services.AddTransient<RequestLoggingMiddleware>();
builder.Services.AddTransient<ResponseHeaderMiddleware>();

// Add to pipeline — ORDER MATTERS
app.UseMiddleware<ResponseHeaderMiddleware>();   // outermost wrapper
app.UseMiddleware<RequestLoggingMiddleware>();   // logs every request that reaches it
```

**Java parallel:**
```java
@Bean
public FilterRegistrationBean<RequestLoggingFilter> loggingFilter() {
    var reg = new FilterRegistrationBean<>(new RequestLoggingFilter());
    reg.setOrder(1);
    return reg;
}
```

---

## Endpoints

| Method | Route | Notes |
|--------|-------|-------|
| `GET` | `/middleware/ping` | Returns `{ message: "pong" }` — used to verify header injection |
| `GET` | `/middleware/slow` | 10 ms delay — verifies elapsed-time logging |

---

## Project Structure (new / changed files)

```
Lesson/
  Middleware/
    RequestLoggingMiddleware.cs  NEW  IMiddleware — logs method, path, status, elapsed
    ResponseHeaderMiddleware.cs  NEW  IMiddleware — injects X-Powered-By header
  Controllers/
    MiddlewareDemoController.cs  NEW  /middleware/ping + /middleware/slow
  Program.cs                          + middleware DI registrations + UseMiddleware calls
Lesson.Tests/
  MiddlewareBasicTests.cs        NEW  7 integration tests
```

---

## Tests

```bash
dotnet test --filter "FullyQualifiedName~MiddlewareBasicTests"
# 7 tests — all pass
```

| Test | What it verifies |
|---|---|
| `Ping_ResponseContainsXPoweredByHeader` | Header is present |
| `Ping_XPoweredByHeader_ContainsExpectedValue` | Header value contains "ASP.NET Core" |
| `AnyEndpoint_ResponseContainsXPoweredByHeader` | Header applies to all routes |
| `Ping_Returns200_MiddlewareDoesNotBreakPipeline` | Middleware is transparent |
| `Ping_ResponseBody_IsCorrect` | Body is unchanged by middleware |
| `Slow_Returns200_AfterDelay` | Middleware works with delayed responses |
| `UnknownRoute_Returns404_MiddlewareStillAddsHeader` | Header added even on 404 responses |

---

## Exercises

1. Add a `CorrelationIdMiddleware` that reads `X-Correlation-Id` from the request (or generates a new `Guid` if absent) and echoes it back in the response headers.
2. Change `ResponseHeaderMiddleware` to add a `Cache-Control: no-store` header and verify with a test.
3. Add a middleware that short-circuits the pipeline for requests to `/health` and returns `200 OK` directly — bypassing the router and all downstream middleware.
4. Register `RequestLoggingMiddleware` **before** `ResponseHeaderMiddleware` and observe how the log output changes (the status code logged will still be correct because both happen after `next()`).


> **Branch:** `lesson/05-linq/c-advanced`
> **Prerequisites:** Lesson 05-B (IEnumerable vs IQueryable, GroupBy, Join, SelectMany, let)

---

## What you will learn

| Topic | C# | Java parallel |
|---|---|---|
| Custom LINQ extensions | `this IEnumerable<T>` extension methods | static utility methods (no dot-notation) |
| `Aggregate` | general-purpose fold / reduce | `stream().reduce(identity, accumulator)` |
| `Zip` | pair two sequences by index | `IntStream.range + get(i)` |
| `Chunk` | split into fixed-size pages | Guava `Lists.partition` |
| `AsParallel` (PLINQ) | CPU-bound parallelism over thread pool | `stream().parallel()` |
| Expression trees | `Expression<Func<T,bool>>` — build predicates at runtime | Reflection + `Predicate<T>` |
| `IAsyncEnumerable<T>` | async streaming with `await foreach` | Project Reactor `Flux<T>` |

---

## 1. Custom LINQ Extension Methods

Extending `IEnumerable<T>` with a static class makes reusable pipeline steps feel native:

```csharp
public static class ProductExtensions
{
    public static IEnumerable<Product> InStock(
        this IEnumerable<Product> source, int minStock = 1)
        => source.Where(p => p.Stock >= minStock);

    public static IEnumerable<Product> PriceAbove(
        this IEnumerable<Product> source, decimal min)
        => source.Where(p => p.Price >= min);
}

// Usage — reads like built-in LINQ
var result = products.InStock().PriceAbove(50m).MostExpensive(3).ToList();
```

**Java parallel:** static helper methods work but break the fluent chain:
`ProductUtils.mostExpensive(ProductUtils.priceAbove(products, 50), 3)`

---

## 2. Aggregate — General-Purpose Fold

`Aggregate` is the universal accumulator operator (like `reduce` in functional programming):

```csharp
// Sum all inventory values
decimal total = products.Aggregate(0m, (acc, p) => acc + p.Price * p.Stock);

// Build a comma-separated string
string catalogue = products
    .OrderBy(p => p.Name)
    .Aggregate(string.Empty, (acc, p) => acc.Length == 0 ? p.Name : acc + ", " + p.Name);
```

For common aggregates (`Sum`, `Average`, `Max`, `Min`, `Count`) prefer the specialised
operators — they are more readable and EF Core can translate them to SQL.

**Java:** `stream().reduce(BigDecimal.ZERO, (acc, p) -> acc.add(p.getPrice()), BigDecimal::add)`

---

## 3. Zip — Pair Two Sequences by Index

```csharp
var sorted = products.OrderByDescending(p => p.Price);
var ranks  = Enumerable.Range(1, products.Count);

var ranked = sorted
    .Zip(ranks, (p, rank) => new RankedProduct(rank, p.Name, p.Price))
    .ToList();
// ? [ { Rank=1, Name="Laptop Pro", Price=1299 }, … ]
```

`Zip` stops at the shorter sequence. Three-sequence overloads exist:
`a.Zip(b, c)` returns value tuples `(a[i], b[i], c[i])`.

**Java:** `IntStream.range(0, Math.min(a.size(), b.size())).mapToObj(i -> new Pair(a.get(i), b.get(i)))`

---

## 4. Chunk — Split into Fixed-Size Pages

Introduced in .NET 6:

```csharp
// Splits 10 products into pages of 3: [[p1,p2,p3],[p4,p5,p6],[p7,p8,p9],[p10]]
Product[][] pages = products.Chunk(3).ToArray();
```

`Chunk` is ideal for batch-processing large sequences without loading everything at once.

**Java:** `Guava: Lists.partition(list, 3)` or a custom `IntStream` splitter.

---

## 5. AsParallel — PLINQ Basics

```csharp
var expensive = products
    .AsParallel()                            // distribute work across ThreadPool
    .Where(p => p.Price > minPrice)          // runs on multiple threads
    .OrderBy(p => p.Name)                    // re-serialise before output
    .ToList();
```

Guidelines:
- Use for **CPU-bound** work on large collections (> ~1 000 items as a rough threshold).
- For **I/O-bound** work, use `async/await` — PLINQ blocks threads.
- Results are non-deterministic unless you add `AsOrdered()` or a final `OrderBy`.

**Java:** `stream().parallel()` — same concept and same caveats.

---

## 6. Expression Trees — Intro

An `Expression<Func<T, bool>>` stores a LINQ query as a **data structure** (AST) rather
than a compiled delegate. EF Core reads this tree to generate SQL.

```csharp
// Build: p => p.Price < maxPrice
var param    = Expression.Parameter(typeof(Product), "p");
var property = Expression.Property(param, nameof(Product.Price));
var constant = Expression.Constant(maxPrice, typeof(decimal));
var body     = Expression.LessThan(property, constant);
var lambda   = Expression.Lambda<Func<Product, bool>>(body, param);

// Compile and use as a normal delegate
var predicate = lambda.Compile();
var result = products.Where(predicate).ToList();
```

This pattern powers dynamic query builders, AutoMapper projections, and EF Core itself.

**Java parallel:** no direct equivalent; closest is reflection-based predicate construction.

---

## 7. IAsyncEnumerable\<T\> — Async Streaming

`IAsyncEnumerable<T>` lets you produce and consume items **one at a time** asynchronously,
without buffering the entire result:

```csharp
// Producer — async iterator method
public async IAsyncEnumerable<Product> StreamProductsAsync(decimal maxPrice,
    [EnumeratorCancellation] CancellationToken ct = default)
{
    foreach (var p in products.Where(p => p.Price <= maxPrice))
    {
        await Task.Delay(0, ct); // simulate async source (DB cursor, HTTP stream, …)
        yield return p;
    }
}

// Consumer
await foreach (var p in service.StreamProductsAsync(100m))
    Console.WriteLine(p.Name);
```

Use cases: database cursors with EF Core (`IAsyncEnumerable<T>` from `ToAsyncEnumerable`),
file streaming, server-sent events, gRPC streaming.

**Java parallel:** Project Reactor `Flux<T>` or Java 9 `Flow.Publisher<T>`.

---

## Endpoints

| Method | Route | Notes |
|--------|-------|-------|
| `GET` | `/linq/advanced/top-in-stock?minPrice=50&topN=3` | Custom extensions chained |
| `GET` | `/linq/advanced/inventory-value` | `Aggregate` — sum of Price × Stock |
| `GET` | `/linq/advanced/catalogue` | `Aggregate` — comma-separated names |
| `GET` | `/linq/advanced/ranked` | `Zip` — products with price rank |
| `GET` | `/linq/advanced/chunks?pageSize=3` | `Chunk` — pages of products |
| `GET` | `/linq/advanced/parallel?minPrice=50` | `AsParallel` filter |
| `GET` | `/linq/advanced/expression-tree?maxPrice=100` | Runtime-built predicate |
| `GET` | `/linq/advanced/stream?maxPrice=100` | `IAsyncEnumerable<T>` streaming |

---

## Project Structure (new / changed files)

```
Lesson/
  Extensions/
    ProductExtensions.cs       NEW  InStock, Cheapest, MostExpensive, PriceAbove
  Models/
    RankedProduct.cs           NEW  result record for Zip demo
  Services/
    LinqAdvancedService.cs     NEW  Aggregate, Zip, Chunk, PLINQ, ExprTree, IAsyncEnumerable
  Controllers/
    LinqAdvancedController.cs  NEW  /linq/advanced/* endpoints
  Program.cs                        + LinqAdvancedService registered as singleton
Lesson.Tests/
  LinqAdvancedTests.cs         NEW  13 integration tests
```

---

## Tests

```bash
dotnet test --filter "FullyQualifiedName~LinqAdvancedTests"
# 13 tests — all pass
```

| Test | What it verifies |
|---|---|
| `GetTopInStock_ReturnsAtMostTopN` | Custom extension `MostExpensive` honours `topN` |
| `GetTopInStock_AllProductsExceedMinPrice` | Custom extension `PriceAbove` filters correctly |
| `GetInventoryValue_MatchesManualCalculation` | `Aggregate` sum equals manual calculation |
| `GetCatalogue_ContainsAllProductNames` | `Aggregate` string fold contains every name |
| `GetRanked_CountMatchesProductCount` | `Zip` produces one entry per product |
| `GetRanked_Rank1HasHighestPrice` | Rank 1 corresponds to the most expensive product |
| `GetChunks_PageSize3_ProducesCorrectNumberOfChunks` | `Chunk` creates ceiling(10/3) = 4 pages |
| `GetChunks_TotalItemsEqualsProductCount` | All items preserved across chunks |
| `GetParallel_SameIdsAsSequentialFilter` | `AsParallel` returns same set as sequential |
| `GetByExpressionTree_AllProductsBelowMaxPrice` | Expression-tree predicate filters correctly |
| `GetByExpressionTree_SameResultAsDirectFilter` | Tree result matches hard-coded LINQ filter |
| `StreamProducts_AllNamesAreBelowMaxPrice` | `IAsyncEnumerable` respects max-price filter |
| `StreamProducts_CountMatchesExpected` | Streamed item count matches direct count |

---

## Exercises

1. Add a `SumBy<T>` generic extension method on `IEnumerable<T>` that takes a `Func<T, decimal>` selector — a miniature reimplementation of `Sum`.
2. Use `Aggregate` with a seed of `new Dictionary<string, decimal>()` to build a category ? total-price map in a single pass.
3. Add `AsOrdered()` to the PLINQ pipeline and verify the test still passes — then remove it and observe whether order is preserved across runs.
4. Modify `StreamProductsAsync` to introduce a real `await Task.Delay(1)` and test the endpoint with a short cancellation token to observe `OperationCanceledException` propagation.
5. Build a more complex expression tree: `p => p.Price < maxPrice && p.Category == category` using `Expression.AndAlso`.


> **Branch:** `lesson/05-linq/b-intermediate`
> **Prerequisites:** Lesson 05-A (Where, Select, OrderBy, FirstOrDefault, deferred execution)

---

## What you will learn

| Topic | C# LINQ | Java parallel |
|---|---|---|
| `IEnumerable<T>` vs `IQueryable<T>` | in-memory iteration vs lazy expression tree | `List` stream vs `JpaSpecificationExecutor` |
| `GroupBy` | aggregate by key | `Collectors.groupingBy` |
| `Join` | inner equi-join | `stream().flatMap` + Map lookup |
| `SelectMany` | flatten nested sequences | `stream().flatMap` |
| `let` (query syntax) | introduce an intermediate value | multi-step `.map()` chain |
| Anonymous types | `new { p.Name, p.Price }` | no direct equivalent (use records/tuples) |
| Pipeline chaining | compose operators before materialising | chained stream intermediate ops |

---

## 1. IEnumerable\<T\> vs IQueryable\<T\>

`IEnumerable<T>` iterates in memory; `IQueryable<T>` builds an expression tree that a
provider (EF Core, LINQ to SQL, …) translates to SQL before touching the database.

```csharp
// IEnumerable path — ALL rows loaded first, then filtered in C#
IEnumerable<Product> all = Products.ToList();           // materialise
var result = all.Where(p => p.Category == cat).ToList();

// IQueryable-equivalent — filter is composed before materialisation
IEnumerable<Product> lazy = Products;                   // no iteration yet
lazy = lazy.Where(p => p.Category == cat);              // deferred
var result = lazy.ToList();                             // single pass, only matching elements
```

With a real `DbSet<T>` the lazy path generates `WHERE Category = @cat` in SQL;
the in-memory path loads every row and discards non-matching ones in C#.

**Rule:** keep filters on `IQueryable` until you need the data.

---

## 2. GroupBy

```csharp
var stats = products
    .GroupBy(p => p.Category)
    .Select(g => new CategorySummary(
        g.Key,
        g.Count(),
        g.Sum(p => p.Price),
        g.Average(p => (double)p.Price)))
    .OrderBy(s => s.Category)
    .ToList();
```

**Java:** `stream().collect(Collectors.groupingBy(Product::getCategory, Collectors.counting()))`

---

## 3. Join — Equi-Join Two Sequences

```csharp
var lines = orders.Join(
    products,
    o => o.ProductId,           // outer key
    p => p.Id,                  // inner key
    (o, p) => new OrderLine(
        o.Id, o.CustomerId, p.Name, p.Category,
        p.Price, o.Quantity, p.Price * o.Quantity))
    .ToList();
```

**Java:** `orders.stream().flatMap(o -> products.stream().filter(p -> p.getId() == o.getProductId()).map(p -> new OrderLine(...)))`
(prefer a `Map<Id, Product>` lookup for O(1) performance)

---

## 4. SelectMany — Flattening

```csharp
// Each group contributes multiple strings; SelectMany flattens them into one sequence
var labels = products
    .GroupBy(p => p.Category)
    .OrderBy(g => g.Key)
    .SelectMany(g => g.Select(p => $"[{g.Key}] {p.Name}"))
    .ToList();
```

**Java:** `categories.stream().flatMap(g -> g.getProducts().stream().map(p -> "[" + g.getKey() + "] " + p.getName()))`

---

## 5. let Clause (Query Syntax)

`let` introduces a named intermediate value inside a query-syntax expression,
avoiding recomputing the same expression in `where` and `select`:

```csharp
var discounted =
    (from p in products
     let d = p.Price * (1 - discountRate)   // compute once
     where d < maxDiscountedPrice            // reuse in filter
     orderby d
     select new DiscountedProduct(p.Name, p.Price, d))  // reuse in projection
    .ToList();
```

**Java:** no `let` keyword; use a `.map()` step that projects to a temporary holder record.

---

## 6. Anonymous Types

```csharp
var projection = products.Select(p => new { p.Name, p.Price }).ToList();
// Type is compiler-generated; only usable within the same method.
```

For cross-method use, prefer named records or tuples.
In EF Core queries, anonymous types in `Select` translate to a `SELECT Name, Price` SQL projection.

---

## Endpoints

| Method | Route | Notes |
|--------|-------|-------|
| `GET` | `/linq/filter-in-memory?category=` | IEnumerable path (materialise then filter) |
| `GET` | `/linq/filter-lazy?category=` | IQueryable-equivalent (filter then materialise) |
| `GET` | `/linq/categories/summary` | `GroupBy` ? category stats |
| `GET` | `/linq/orders/lines` | `Join` products and orders |
| `GET` | `/linq/products/labels` | `SelectMany` flattened labels |
| `GET` | `/linq/products/discounted?discountRate=0.10&maxDiscountedPrice=100` | `let` clause demo |
| `GET` | `/linq/orders/top?topN=3` | Chained pipeline — top N by line total |

---

## Project Structure (new / changed files)

```
Lesson/
  Models/
    Order.cs                 NEW  seed order records for Join demo
    LinqIntermediateDtos.cs  NEW  CategorySummary, OrderLine, DiscountedProduct
  Services/
    LinqIntermediateService.cs  NEW  GroupBy, Join, SelectMany, let, chaining
  Controllers/
    LinqIntermediateController.cs  NEW  /linq/filter-*, /linq/categories/summary,
                                        /linq/orders/lines, /linq/products/labels,
                                        /linq/products/discounted, /linq/orders/top
  Program.cs                      + LinqIntermediateService registered as singleton
Lesson.Tests/
  LinqIntermediateTests.cs  NEW  13 integration tests
```

---

## Tests

```bash
dotnet test --filter "FullyQualifiedName~LinqIntermediateTests"
# 13 tests — all pass
```

| Test | What it verifies |
|---|---|
| `FilterInMemory_SameResultAsFilterLazy_ForSameCategory` | Both paths return identical product IDs |
| `FilterLazy_ReturnsOnlyMatchingCategory` | Lazy filter returns only the requested category |
| `GetCategorySummaries_ReturnsAllCategories` | GroupBy covers every distinct category |
| `GetCategorySummaries_CountsAreCorrect` | Per-category counts match seed data |
| `GetCategorySummaries_TotalValueMatchesSeedData` | Sum of all totals equals sum of all prices |
| `GetOrderLines_CountMatchesSeedOrders` | Join produces one row per order |
| `GetOrderLines_LineTotalIsUnitPriceTimesQuantity` | LineTotal = UnitPrice × Quantity |
| `GetProductLabels_CountMatchesProductCount` | SelectMany flattens to exactly 10 labels |
| `GetProductLabels_EachLabelContainsCategoryAndName` | Format `[Category] Name` respected |
| `GetDiscounted_AllDiscountedPricesAreBelowMaxPrice` | let filter applied correctly |
| `GetDiscounted_DiscountedPriceEqualsOriginalTimesRate` | Computed discount is accurate |
| `GetTopOrders_Top2_ReturnsExactly2` | Take(2) respected |
| `GetTopOrders_AreOrderedByLineTotalDescending` | Chaining keeps correct sort order |

---

## Exercises

1. Add `GET /linq/categories/summary?minCount=2` — filter `CategorySummary` rows where `Count >= minCount` using a chained `.Where()` after `GroupBy`.
2. Add `GET /linq/orders/by-customer/{customerId}` — use `Join` + `Where` to return only order lines for a specific customer.
3. Rewrite `GetOrderLines` using query syntax with an explicit `join … in … on … equals …` clause and compare readability.
4. Replace `SelectMany` in `GetAllProductLabels` with a nested `foreach` loop and verify the output is identical — then appreciate the brevity of `SelectMany`.


> **Branch:** `lesson/05-linq/a-basic`
> **Prerequisites:** Lesson 04-C (Raw SQL, Compiled Queries, Split Queries)

---

## What you will learn

| Topic | C# LINQ | Java parallel |
|---|---|---|
| Method syntax | `.Where().Select().ToList()` | `stream().filter().map().collect()` |
| Query syntax | `from p in … where … select p` | no direct equivalent (method chains only) |
| `Where` | filter elements | `stream().filter(…)` |
| `Select` | project / transform elements | `stream().map(…)` |
| `OrderBy` / `OrderByDescending` | sort; `ThenBy` for secondary key | `stream().sorted(Comparator…)` |
| `FirstOrDefault` | first match or `null` — never throws | `stream().findFirst().orElse(null)` |
| `ToList` | terminal — materialises the pipeline | `stream().collect(toList())` |
| Deferred execution | pipeline is lazy — work happens at the terminal operator | Java Streams are also lazy |

---

## 1. Method Syntax vs Query Syntax

LINQ provides two syntaxes that compile to identical IL.

```csharp
// Method syntax
var result = products
    .Where(p => p.Category == "Electronics")
    .OrderBy(p => p.Price)
    .Select(p => p.Name)
    .ToList();

// Query syntax (SQL-like)
var result = (from p in products
              where p.Category == "Electronics"
              orderby p.Price
              select p.Name).ToList();
```

Choose whichever reads more clearly for the task. Method syntax is more common for simple
pipelines; query syntax shines when using `let`, `join`, or `group … by`.

**Java parallel:** Java Streams only have method chains — there is no query-syntax equivalent.

---

## 2. Where — Filtering

```csharp
var electronics = products.Where(p => p.Category == "Electronics").ToList();
```

`Where` accepts a predicate and returns every element for which it is `true`.
The predicate is not evaluated until a terminal operator materialises the pipeline.

**Java:** `stream().filter(p -> p.getCategory().equals("Electronics")).collect(toList())`

---

## 3. Select — Projection

```csharp
// Project to a value tuple
var nameAndPrice = products.Select(p => (p.Name, p.Price)).ToList();
```

Only the data you need is materialised; when used with `IQueryable` (EF Core) only those
columns are included in the SQL `SELECT`.

**Java:** `stream().map(p -> new NamePrice(p.getName(), p.getPrice())).collect(toList())`

---

## 4. OrderBy / OrderByDescending

```csharp
var sorted = products
    .OrderByDescending(p => p.Price)  // primary sort key
    .ThenBy(p => p.Name)              // secondary sort key (stable)
    .ToList();
```

`ThenBy` / `ThenByDescending` add secondary keys.
Do **not** chain multiple `OrderBy` calls — each resets the sort order.

**Java:** `stream().sorted(Comparator.comparing(Product::getPrice).reversed().thenComparing(Product::getName))`

---

## 5. FirstOrDefault — Safe Single-Element Lookup

```csharp
Product? found = products.FirstOrDefault(p => p.Id == id);
// Returns null if no match — never throws.
```

| Method | No match | Multiple matches |
|---|---|---|
| `FirstOrDefault` | `null` / `default` | returns first |
| `First` | throws `InvalidOperationException` | returns first |
| `SingleOrDefault` | `null` / `default` | throws |
| `Single` | throws | throws |

**Java:** `stream().filter(p -> p.getId() == id).findFirst().orElse(null)`

---

## 6. Deferred Execution

Building a LINQ pipeline does **not** iterate the source — that work is deferred until a
terminal operator is called.

```csharp
// Steps 1-3: build the pipeline (no iteration yet)
IEnumerable<string> query = products
    .Where(p => p.Price <= 50)   // deferred
    .OrderBy(p => p.Price)       // deferred
    .Select(p => p.Name);        // deferred

// Step 4: terminal operator — iterates ONCE and produces List<string>
List<string> result = query.ToList();
```

Consequence: modifying the source between building and materialising the query is reflected
in the result. Materialise early with `ToList()` / `ToArray()` when you want a snapshot.

**Java parallel:** Java Streams are also lazy. Unlike C# queries, a Java Stream **cannot be reused**
after a terminal operation has been called.

---

## Endpoints

| Method | Route | Notes |
|--------|-------|-------|
| `GET` | `/linq/products` | All products; optional `?category=` filter |
| `GET` | `/linq/products/query-syntax?category=` | Same filter via query syntax |
| `GET` | `/linq/products/name-price` | `Select` projection — name + price only |
| `GET` | `/linq/products/by-price-desc` | `OrderByDescending` + `ThenBy` |
| `GET` | `/linq/products/{id}` | `FirstOrDefault` — 404 if not found |
| `GET` | `/linq/products/affordable?maxPrice=50` | Deferred pipeline materialised at `ToList` |

---

## Project Structure (new / changed files)

```
Lesson/
  Models/
    Product.cs               NEW  simple in-memory record (no EF / database)
  Services/
    LinqService.cs           NEW  static seed data + LINQ demo methods
  Controllers/
    LinqDtos.cs              NEW  ProductResponse, NamePriceDto
    LinqController.cs        NEW  /linq/* endpoints
  Program.cs                      + LinqService registered as singleton
Lesson.Tests/
  LinqBasicTests.cs          NEW  10 integration tests
```

---

## Tests

```bash
dotnet test --filter "FullyQualifiedName~LinqBasicTests"
# 10 tests — all pass
```

| Test | What it verifies |
|---|---|
| `FilterByCategory_Electronics_ReturnsOnlyElectronics` | `Where` filters correctly |
| `FilterByCategory_UnknownCategory_ReturnsEmpty` | `Where` with no match returns empty |
| `FilterByCategory_NoCategory_ReturnsAll` | No filter returns all 10 products |
| `QuerySyntax_SameResultAsMethodSyntax` | Both syntaxes produce identical results |
| `GetNameAndPrice_ReturnsOnlyNameAndPrice` | `Select` projection returns correct shape |
| `GetByPriceDescending_FirstItemIsHighestPrice` | First item has the maximum price |
| `GetByPriceDescending_IsSorted` | Entire list is in descending price order |
| `FindById_ExistingId_ReturnsProduct` | `FirstOrDefault` finds the right product |
| `FindById_MissingId_ReturnsNotFound` | `FirstOrDefault` returns `null` ? 404 |
| `GetAffordableNames_MaxPrice50_OnlyCheapProducts` | Full deferred pipeline matches expected names |

---

## Exercises

1. Add `GET /linq/products/top/{n}` that uses `Take(n)` to return the `n` most expensive products.
2. Replace the `ToList()` terminal in `GetAffordableProductNames` with `ToArray()` and observe that the tests still pass.
3. Add a `Skip` + `Take` overload to `/linq/products?page=1&pageSize=3` to practice manual pagination over an in-memory collection.
4. Try calling `.Where(…)` twice on the same pipeline and verify that both predicates are applied (they are ANDed together in the iteration).

| Compiled query | `EF.CompileAsyncQuery(…)` | `@NamedQuery` / `@NamedNativeQuery` |
| Split query | `.Include(…).AsSplitQuery()` | `@EntityGraph` with `SUBSELECT` fetch |
| Cartesian explosion | single JOIN ? N×M rows | N+1 / cartesian product in JPA `JOIN FETCH` |

---

## 1. FromSqlRaw — Hand-written Parameterised SQL

`FromSqlRaw` lets you write arbitrary SQL while still getting tracked entities back.
EF Core can compose additional LINQ operators (`Where`, `OrderBy`, `Include`, …) on top.

```csharp
var accounts = await db.BankAccounts
    .FromSqlRaw(
        "SELECT * FROM BankAccounts WHERE Balance > {0} AND IsDeleted = 0",
        minBalance)
    .OrderBy(a => a.AccountNumber)
    .ToListAsync();
```

> ?? **SQL injection:** always use `{0}` placeholders (or `SqlParameter` objects).
> Never interpolate user input directly into the string.
> `FromSqlInterpolated` is an alternative that accepts a C# interpolated string safely.

**Java parallel:** `@Query(value = "SELECT * FROM bank_accounts WHERE balance > :min", nativeQuery = true)`

---

## 2. Stored Procedure Calls

On **SQL Server**:

```csharp
var accounts = await db.BankAccounts
    .FromSqlRaw("EXEC sp_GetAccountByNumber {0}", accountNumber)
    .ToListAsync();
```

On **PostgreSQL**:

```csharp
var accounts = await db.BankAccounts
    .FromSqlRaw("SELECT * FROM sp_get_account_by_number({0})", accountNumber)
    .ToListAsync();
```

**SQLite** has no stored-procedure engine, so this lesson uses an equivalent parameterised
`SELECT` and documents the real SP syntax above.

**Java parallel:**
```java
@Procedure("sp_GetAccountByNumber")
BankAccount getByNumber(String accountNumber);
// or:
entityManager.createNativeQuery("CALL sp_GetAccountByNumber(?)", BankAccount.class)
             .setParameter(1, accountNumber)
             .getSingleResult();
```

---

## 3. Compiled Queries

Every time you call a LINQ query EF Core translates the expression tree to SQL.
`EF.CompileAsyncQuery` does that translation **once at startup** and caches the result,
eliminating the per-call overhead on hot paths (thousands of calls per second).

```csharp
// Declared as a static field — compiled once per AppDomain.
private static readonly Func<BankingDbContext, string, IAsyncEnumerable<BankAccount>>
    _getByNumber = EF.CompileAsyncQuery(
        (BankingDbContext ctx, string number) =>
            ctx.BankAccounts.Where(a => a.AccountNumber == number));

// Usage — no translation overhead on subsequent calls.
await foreach (var account in _getByNumber(db, accountNumber))
    return account;
```

**Java parallel:** Hibernate `@NamedQuery` / `@NamedNativeQuery` — compiled during
`SessionFactory` bootstrap and reused for every execution.

---

## 4. Split Queries — Preventing Cartesian Explosion

When you `Include` a collection navigation on multiple parent rows, EF Core's default
single-JOIN strategy produces a **Cartesian product**:

```
2 accounts × 5 transactions = 10 result rows transferred
(even though only 7 logical rows exist)
```

With large collections (100 parents × 1 000 children) this multiplies to **100 000 rows**
over the wire for what is logically 1 100 rows of data.

`AsSplitQuery()` fires two separate SELECTs and stitches the results in memory:

```csharp
var accounts = await db.BankAccounts
    .Include(a => a.Transactions)
    .AsSplitQuery()          // ? two queries instead of one JOIN
    .OrderBy(a => a.AccountNumber)
    .ToListAsync();
// SQL 1: SELECT * FROM BankAccounts
// SQL 2: SELECT * FROM Transactions WHERE BankAccountId IN (1, 2, …)
```

**Trade-off:** two round-trips instead of one; results may be slightly inconsistent if
another transaction modifies data between the two SELECTs.  Choose split queries when
collection sizes make the Cartesian product impractical.

**Java parallel:** `@EntityGraph` with `@EntityGraph.EntityGraphType.FETCH` and
`fetchType = SUBSELECT` on the collection mapping.

---

## Endpoints

| Method | Route | Notes |
|--------|-------|-------|
| `GET` | `/accounts/raw?minBalance=0` | `FromSqlRaw` parameterised query |
| `GET` | `/accounts/by-number-sp/{number}` | Stored-procedure simulation |
| `GET` | `/accounts/by-number-compiled/{number}` | Compiled query lookup |
| `GET` | `/accounts/with-transactions` | `AsSplitQuery` — accounts + transactions |

---

## Project Structure (new / changed files)

```
Lesson/
  Repositories/
    IAccountRepository.cs     + GetByRawSqlAsync, GetByNumberStoredProcAsync,
                                GetByNumberCompiledAsync, GetWithTransactionsSplitAsync
    AccountRepository.cs      implements the above; compiled query as static field
  Controllers/
    AccountDtos.cs            + TransactionSummaryDto
    AccountsController.cs     + /raw, /by-number-sp, /by-number-compiled,
                                /with-transactions endpoints
Lesson.Tests/
  AccountsControllerRawSqlTests.cs  NEW  10 integration tests
```

---

## Tests

```bash
dotnet test --filter "FullyQualifiedName~AccountsControllerRawSqlTests"
# 10 tests — all pass
```

| Test | What it verifies |
|---|---|
| `GetByRawSql_BelowAllBalances_ReturnsAllAccounts` | `FromSqlRaw` with threshold 0 returns all seeded accounts |
| `GetByRawSql_AboveAllBalances_ReturnsEmpty` | impossibly high threshold ? empty list |
| `GetByRawSql_OnlyReturnsAccountsAboveThreshold` | all returned accounts exceed the threshold |
| `GetByNumberStoredProc_ExistingAccount_ReturnsAccount` | SP simulation returns the right account |
| `GetByNumberStoredProc_UnknownAccount_ReturnsNotFound` | 404 for unknown account number |
| `GetByNumberCompiled_ExistingAccount_ReturnsAccount` | compiled query finds seeded account |
| `GetByNumberCompiled_UnknownAccount_ReturnsNotFound` | 404 for unknown account number |
| `GetByNumberCompiled_CalledTwice_BothReturnSameResult` | compiled query is idempotent |
| `GetWithTransactions_ReturnsAccountsWithTransactions` | split query returns accounts |
| `GetWithTransactions_SeededTransactions_ArePresent` | transactions are present in the split-query result |

---

## Exercises

1. Replace `FromSqlRaw` in `GetByRawSqlAsync` with `FromSqlInterpolated` and observe how EF Core automatically prevents SQL injection.
2. Add a `ExecuteSqlRaw` call to bulk-deactivate all accounts of a given type:
   `UPDATE BankAccounts SET IsActive = 0 WHERE AccountType = {0}`.
3. Benchmark (using BenchmarkDotNet) the compiled query vs. a non-compiled equivalent to
   measure the translation overhead on a tight loop.
4. Enable `QuerySplittingBehavior.SplitQuery` globally in `DbContextOptions` and verify that all `Include` calls now use split queries by default, then override one back to `AsSingleQuery()`.


> **Branch:** `lesson/04-ef-advanced/b-intermediate`
> **Prerequisites:** Lesson 04-A (Navigation properties, Include / ThenInclude)

---

## What you will learn

| Topic | C# / EF Core | Java / Spring Boot parallel |
|---|---|---|
| Pagination | `Skip` / `Take` | `PageRequest.of(page, size)` ? `Page<T>` |
| Projection | `.Select(a => new Dto(...))` | DTO projection with `@Query` + constructor expression |
| GroupBy aggregate | `.GroupBy(...).Select(g => ...)` | `@Query("SELECT ... GROUP BY ...")` |
| Existential check | `AnyAsync(predicate)` | `repository.existsBy...()` |
| Universal check | `AllAsync(predicate)` | custom `@Query` with NOT EXISTS |
| Scalar count | `CountAsync(predicate)` | `repository.countBy...()` |
| IQueryable vs IEnumerable | query tree vs in-memory iteration | `JpaSpecificationExecutor` vs stream |

---

## 1. IQueryable vs IEnumerable

**Key concept:** `IQueryable<T>` is an unevaluated expression tree — EF Core composes SQL
from it and executes when you call a terminal operator (`ToListAsync`, `FirstOrDefaultAsync`, …).
`IEnumerable<T>` is in-memory — all rows are loaded before filtering/projecting.

```csharp
// IQueryable — SQL WHERE is added before the query is sent
IQueryable<BankAccount> query = db.BankAccounts;
query = query.Where(a => a.AccountType == "Savings"); // no DB round-trip yet
var list = await query.ToListAsync();                  // ONE SQL query with WHERE

// IEnumerable — loads ALL rows, then filters in C#
IEnumerable<BankAccount> all = await db.BankAccounts.ToListAsync();
var savings = all.Where(a => a.AccountType == "Savings"); // in-memory!
```

**Rule:** keep your queries as `IQueryable` until you need the data.

**Java parallel:** `JpaSpecificationExecutor<T>` builds a query spec lazily; calling
`findAll(spec)` executes it. Collecting to a `List<>` and then streaming is the in-memory equivalent.

---

## 2. Pagination with Skip / Take

```csharp
var items = await db.BankAccounts
    .OrderBy(a => a.AccountNumber)      // ORDER BY is required before OFFSET
    .Skip((page - 1) * pageSize)        // SQL: OFFSET
    .Take(pageSize)                     // SQL: LIMIT / FETCH NEXT
    .ToListAsync();
```

Combined with a total count this gives a `PagedResult<T>`:

```csharp
int total = await baseQuery.CountAsync();
// ... Skip / Take ...
return new PagedResult<T>(items, total, page, pageSize);
```

**Java parallel:** `repository.findAll(PageRequest.of(page - 1, size))` returns a `Page<T>`
with `.getContent()`, `.getTotalElements()`, and `.getTotalPages()`.

---

## 3. Projection with Select

Only fetch the columns you actually need:

```csharp
var dtos = await db.BankAccounts
    .Select(a => new AccountSummaryDto(
        a.Id, a.AccountNumber, a.OwnerName, a.AccountType, a.Balance, a.IsActive))
    .ToListAsync();
// SQL: SELECT Id, AccountNumber, OwnerName, AccountType, Balance, IsActive FROM BankAccounts
```

Audit / concurrency columns (`RowVersion`, `CreatedAt`, `UpdatedAt`) are never touched.

**Java parallel:** constructor expression in JPQL:
`SELECT new com.example.dto.AccountSummaryDto(a.id, a.accountNumber, ...) FROM BankAccount a`

---

## 4. GroupBy Aggregate

```csharp
// Works fully in SQL on SQL Server / PostgreSQL
var stats = await db.BankAccounts
    .GroupBy(a => a.AccountType)
    .Select(g => new AccountTypeStatDto(
        g.Key, g.Count(), g.Sum(a => a.Balance), g.Average(a => (double)a.Balance)))
    .ToListAsync();
```

> **SQLite caveat:** SQLite's `decimal` support for aggregates is limited.
> The implementation fetches `(AccountType, Balance)` columns via `IQueryable` projection,
> then completes the grouping in C# memory — illustrating the intentional IQueryable ? IEnumerable handoff:
>
> ```csharp
> var rows = await db.BankAccounts
>     .Select(a => new { a.AccountType, a.Balance })
>     .ToListAsync();                                   // IQueryable terminates here
>
> return rows
>     .GroupBy(a => a.AccountType)                     // IEnumerable GroupBy in C#
>     .Select(g => new AccountTypeStatDto(...))
>     .ToList();
> ```

**Java parallel:** `@Query("SELECT a.accountType, COUNT(a), SUM(a.balance), AVG(a.balance) FROM BankAccount a GROUP BY a.accountType")`

---

## 5. Any / All / Count

```csharp
// Any — SQL: SELECT CASE WHEN EXISTS (...) THEN 1 ELSE 0 END
bool hasHighBalance = await db.BankAccounts.AnyAsync(a => a.Balance > threshold);

// All — SQL: SELECT CASE WHEN NOT EXISTS (... WHERE NOT condition) THEN 1 ELSE 0 END
bool allPositive = await db.BankAccounts.AllAsync(a => a.Balance > 0);

// Count — SQL: SELECT COUNT(*) FROM BankAccounts WHERE IsActive = 1
int active = await db.BankAccounts.CountAsync(a => a.IsActive);
```

None of these load entity rows — they return a single scalar from the database.

**Java parallel:**
- `AnyAsync` ? `repository.existsByBalanceGreaterThan(threshold)`
- `AllAsync` ? custom `@Query` with `NOT EXISTS`
- `CountAsync` ? `repository.countByIsActive(true)`

---

## Endpoints

| Method | Route | Notes |
|--------|-------|-------|
| `GET` | `/accounts/summary?page=1&pageSize=10` | Paginated + projected DTO list |
| `GET` | `/accounts/stats` | GroupBy AccountType with COUNT / SUM / AVG |
| `GET` | `/accounts/any-high-balance?threshold=10000` | AnyAsync — returns `true`/`false` |
| `GET` | `/accounts/all-positive` | AllAsync — returns `true`/`false` |
| `GET` | `/accounts/count?type=Savings` | CountAsync with optional type filter |

---

## Project Structure (new / changed files)

```
Lesson/
  Entities/
    Transaction.cs               NEW  linked to BankAccount (one-to-many)
    BankAccount.cs                    + Transactions navigation collection
  Repositories/
    IAccountRepository.cs             + GetPagedSummariesAsync, GetStatsByTypeAsync,
                                        AnyWithBalanceAboveAsync, AllPositiveBalanceAsync,
                                        CountActiveAsync
    AccountRepository.cs              implements the above; shows IQueryable/IEnumerable split
  Controllers/
    AccountDtos.cs                    + AccountSummaryDto, AccountTypeStatDto, PagedResult<T>
    AccountsController.cs             + /summary, /stats, /any-high-balance,
                                        /all-positive, /count endpoints
  Data/
    BankingDbContext.cs               + DbSet<Transaction>, HasMany/WithOne config,
                                        seed transactions, query filter on Transaction
    Migrations/
      AddTransactions            NEW  Transactions table + seed rows
Lesson.Tests/
  AccountsControllerAdvancedTests.cs  NEW  11 integration tests
```

---

## Tests

```bash
dotnet test --filter "FullyQualifiedName~AccountsControllerAdvancedTests"
# 11 tests — all pass
```

| Test | What it verifies |
|---|---|
| `GetSummary_Page1_ReturnsPagedResult` | Pagination works; page/count fields populated |
| `GetSummary_PageSize1_ReturnsSingleItem` | `Take(1)` returns exactly one item |
| `GetSummary_ProjectionDto_DoesNotExposeAuditFields` | Projection omits audit columns |
| `GetSummary_InvalidPage_ReturnsBadRequest` | `page=0` ? 400 |
| `GetStats_ReturnsGroupedByAccountType` | GroupBy returns non-empty stat rows |
| `GetStats_SeededData_ContainsCheckingAndSavings` | Both seeded types appear |
| `AnyHighBalance_BelowSeedValues_ReturnsTrue` | AnyAsync ? true when threshold is below seed balances |
| `AnyHighBalance_AboveSeedValues_ReturnsFalse` | AnyAsync ? false for impossibly high threshold |
| `AllPositive_SeededAccounts_ReturnsTrue` | AllAsync ? true; all seeded accounts have positive balance |
| `CountActive_NoFilter_ReturnsAllActiveAccounts` | CountAsync ? > 0 |
| `CountActive_FilteredByType_SumsToTotal` | Savings + Checking counts equal total |

---

## Exercises

1. Add a `GET /accounts/summary` sort parameter (`sortBy=balance&desc=true`) using conditional `OrderBy` on `IQueryable`.
2. Add `GET /accounts/stats/transactions` — GroupBy `AccountType` with `SUM` of transaction amounts (requires a join to `Transactions`).
3. Implement server-side GroupBy for a provider that supports it (e.g., SQL Server) and compare the generated SQL to the SQLite fallback.
4. Add a `MinBalance` / `MaxBalance` filter to `/accounts/summary` and observe how adding `.Where()` clauses to an `IQueryable` before `Skip`/`Take` pushes the filter into the SQL `WHERE` clause.

| Eager loading | `.Include(c => c.Accounts)` | `@EntityGraph` / `JOIN FETCH` |
| Chained loading | `.ThenInclude(a => a.Address)` | nested `JOIN FETCH` in JPQL |
| Filtered Include | `.Include(c => c.Accounts.Where(a => a.IsActive))` | `@Query` with WHERE on the join |
| FK assignment | set `account.CustomerId` — EF updates the row | set the `@ManyToOne` field + persist |
| Lazy loading | disabled by default — must call `Include` explicitly | `FetchType.LAZY` (default in JPA) |

---

## 1. Navigation Properties

A navigation property is a C# reference or collection on an entity class that EF Core uses
to represent a relationship in the object model.

EF Core maps this to a foreign key column (`CustomerId`) in `BankAccounts`.

```csharp
// Customer -- the "one" side
public class Customer
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public ICollection<BankAccount> Accounts { get; set; } = [];
}

// BankAccount -- the "many" side
public class BankAccount
{
    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }
}
```

Configure in `OnModelCreating`:

```csharp
modelBuilder.Entity<Customer>()
    .HasMany(c => c.Accounts)
    .WithOne(a => a.Customer)
    .HasForeignKey(a => a.CustomerId)
    .IsRequired(false)
    .OnDelete(DeleteBehavior.SetNull);
```

**Java parallel:**
```java
@OneToMany(mappedBy = "customer", cascade = CascadeType.ALL)
private List<BankAccount> accounts = new ArrayList<>();

@ManyToOne @JoinColumn(name = "customer_id")
private Customer customer;
```

---

## 2. Eager Loading with Include

By default, EF Core does **not** lazy-load navigation properties. You must call `.Include()` explicitly.

```csharp
var customer = await db.Customers
    .Include(c => c.Accounts)
    .FirstOrDefaultAsync(c => c.Id == id);
```

**Java parallel:** `@EntityGraph(attributePaths = "accounts")` or `JOIN FETCH` in JPQL.

---

## 3. ThenInclude — Loading Nested Navigation

```csharp
var customer = await db.Customers
    .Include(c => c.Accounts)
        .ThenInclude(a => a.Address)
    .FirstOrDefaultAsync(c => c.Id == id);
```

**Java parallel:** nested `JOIN FETCH` in JPQL.

---

## 4. Filtered Include

```csharp
var customer = await db.Customers
    .Include(c => c.Accounts.Where(a => a.IsActive))
    .FirstOrDefaultAsync(c => c.Id == id);
```

The WHERE predicate is pushed into the SQL JOIN. The global `IsDeleted` query filter is also
applied automatically to included collections.

---

## 5. FK Assignment (linking records)

```csharp
account.CustomerId = customerId;
await uow.CommitAsync();
// SQL: UPDATE BankAccounts SET CustomerId = @id WHERE Id = @accountId
```

Setting the FK scalar property is enough — EF Core keeps navigation in sync within the same DbContext scope.

---

## Endpoints

| Method | Route | Notes |
|--------|-------|-------|
| `GET` | `/customers` | All customers (no accounts loaded) |
| `GET` | `/customers/{id}/accounts` | Customer + all accounts (Include + ThenInclude) |
| `GET` | `/customers/{id}/accounts/active` | Customer + active accounts (filtered Include) |
| `POST` | `/customers` | Create customer; 201 / 409 on duplicate email |
| `POST` | `/customers/{cid}/accounts/{aid}` | Assign existing account to customer (FK update) |

---

## Project Structure (new / changed files)

```
Lesson/
  Entities/
    Customer.cs                  NEW  "one" side of Customer -> BankAccounts
    BankAccount.cs                    + CustomerId (FK) + Customer? (navigation)
  Repositories/
    ICustomerRepository.cs       NEW  interface with Include-based query methods
    CustomerRepository.cs        NEW  demonstrates Include, ThenInclude, filtered Include
  Controllers/
    CustomerDtos.cs              NEW  CreateCustomerRequest, CustomerResponse
    CustomersController.cs       NEW  CRUD + Include/filtered-Include endpoints
  Data/
    BankingDbContext.cs               + DbSet<Customer>, HasMany/WithOne config, updated seed
    Migrations/
      AddCustomerNavigation      NEW  adds Customers table + CustomerId FK column
Lesson.Tests/
  CustomersControllerTests.cs   NEW  7 integration tests
```

---

## Tests

```bash
dotnet test --filter "FullyQualifiedName~CustomersControllerTests"
# 7 tests — all pass
```

| Test | What it verifies |
|---|---|
| `GetAll_ReturnsSeededCustomers` | Seeded customers are returned |
| `Create_ValidRequest_ReturnsCreated` | 201 + id populated |
| `Create_DuplicateEmail_ReturnsConflict` | 409 on duplicate email |
| `GetWithAccounts_SeededCustomer_ReturnsAccounts` | Include fires — Accounts is not empty |
| `GetWithAccounts_MissingCustomer_ReturnsNotFound` | 404 for unknown customer |
| `GetWithActiveAccounts_ReturnsOnlyActiveAccounts` | Filtered Include — inactive account excluded |
| `AssignAccount_LinksAccountToCustomer` | FK assignment — account appears in customer list |

---

## Exercises

1. Add `GET /customers/{id}/accounts/savings` using a filtered include for `AccountType == "Savings"`.
2. Add `GET /customers?includeAccounts=true` — conditionally apply Include only when requested.
3. Explore `AsSplitQuery()`: replace the default single-JOIN strategy with two separate queries and compare SQL logs.
4. Add a `Transaction` entity linked to `BankAccount` and practice `ThenInclude` two levels deep:
   `Customer -> Accounts -> Transactions`.
