# C# Learning Curriculum — Senior Dev Track (Banking Context)

> **Target audience:** Experienced Java/Spring Boot and C++ developer preparing for a senior C# role in a banking environment.
>
> **Domain used throughout all lessons:** Banking — accounts, customers, transactions.
>
> **Project:** ASP.NET Core 10 Web API (`Lesson/`)

---

## Branch Convention

```
lesson/NN-topic/x-level
```

| Segment | Values |
|---------|--------|
| `NN` | two-digit lesson number (01, 02, …) |
| `topic` | short kebab-case name |
| `x` | `a` = basic · `b` = intermediate · `c` = advanced |

Each part-branch is created **off the previous part** (`a → b → c`), so `git diff lesson/01-endpoint/a-basic..lesson/01-endpoint/b-intermediate` shows exactly what was added.

`master` is kept clean: only the base scaffold + this plan file.

---

## Lesson Map

### Lesson 01 — Endpoint + Service + Client call
**Branches:** `lesson/01-endpoint/a-basic` · `b-intermediate` · `c-advanced`

| Part | Content |
|------|---------|
| **A - Basic** | `AccountController` with `GET /accounts` and `POST /accounts`; `IAccountService` + `AccountService` wired via DI; record/DTO pattern; `IActionResult` vs typed returns |
| **B - Intermediate** | Typed `HttpClient` calling an external exchange-rate API; `IHttpClientFactory`; `async/await` + `CancellationToken`; `IOptions<T>` for base URL config |
| **C - Advanced** | Resilience with **Polly** (retry, circuit breaker); API response caching; client-side timeout policy; `HttpClient` best practices |

**Java parallels:** `@RestController` → `[ApiController]`; `@Service` → registered in `IServiceCollection`; `RestTemplate`/`WebClient` → `HttpClient` / typed client.

---

### Lesson 02 — Configuration & App Customization
**Branches:** `lesson/02-configuration/a-basic` · `b-intermediate` · `c-advanced`

| Part | Content |
|------|---------|
| **A - Basic** | `appsettings.json` sections; reading with `IConfiguration`; environment-specific overrides (`appsettings.Development.json`) |
| **B - Intermediate** | Strongly-typed config with `IOptions<T>`, `IOptionsSnapshot<T>`, `IOptionsMonitor<T>`; validation on startup (`ValidateDataAnnotations`, `ValidateOnStart`) |
| **C - Advanced** | User Secrets; environment variables; Azure Key Vault integration pattern; custom `IConfigurationProvider` |

**Java parallels:** `application.properties` / `@ConfigurationProperties` → `appsettings.json` / `IOptions<T>`.

---

### Lesson 03 — EF Core CRUD (Database Basics)
**Branches:** `lesson/03-ef-crud/a-basic` · `b-intermediate` · `c-advanced`

| Part | Content |
|------|---------|
| **A - Basic** | `DbContext` + `DbSet<T>`; SQLite setup; code-first migrations; basic `CRUD` operations (`Add`, `Find`, `Update`, `Remove`, `SaveChangesAsync`) |
| **B - Intermediate** | Repository pattern (`IAccountRepository`); `async` DB ops throughout; `IQueryable` vs `IEnumerable` distinction; value objects & owned entities |
| **C - Advanced** | Unit of Work pattern; optimistic concurrency (`RowVersion`); soft delete with global query filters; audit fields (`CreatedAt`, `UpdatedBy`) via `SaveChanges` override |

**Java parallels:** `JpaRepository` → EF Core `DbContext`; `@Entity` → POCO with `DbSet`; Flyway/Liquibase → EF Core Migrations.

---

### Lesson 04 — EF Core Advanced Queries
**Branches:** `lesson/04-ef-advanced/a-basic` · `b-intermediate` · `c-advanced`

| Part | Content |
|------|---------|
| **A - Basic** | Navigation properties; `Include` / `ThenInclude`; one-to-many (Customer → Accounts); filtered includes |
| **B - Intermediate** | `GroupBy` aggregates (sum of transactions per account); projections with `Select` into DTOs; `Any`/`All`/`Count`; pagination (`Skip`/`Take`) |
| **C - Advanced** | Raw SQL with `FromSqlRaw` / `ExecuteSqlRaw`; stored procedure calls; compiled queries; split queries for cartesian explosion prevention |

---

### Lesson 05 — LINQ (Collections & DB)
**Branches:** `lesson/05-linq/a-basic` · `b-intermediate` · `c-advanced`

| Part | Content |
|------|---------|
| **A - Basic** | Method syntax vs query syntax; `Where`, `Select`, `OrderBy`, `FirstOrDefault`, `ToList`; deferred execution explained |
| **B - Intermediate** | `IEnumerable<T>` vs `IQueryable<T>`; `GroupBy`, `Join`, `SelectMany`; chaining; `let` clause; anonymous types |
| **C - Advanced** | Custom LINQ extension methods; `Aggregate`, `Zip`, `Chunk`; `AsParallel` (PLINQ) basics; expression trees (intro); `IAsyncEnumerable<T>` for streaming |

**Java parallels:** Java Streams → LINQ; `stream().filter().map().collect()` → `.Where().Select().ToList()`.

---

### Lesson 06 — Middleware & Action Filters (Interceptors)
**Branches:** `lesson/06-middleware/a-basic` · `b-intermediate` · `c-advanced`

| Part | Content |
|------|---------|
| **A - Basic** | Custom `IMiddleware`; request/response logging middleware; middleware ordering in `Program.cs` |
| **B - Intermediate** | `IActionFilter` / `IAsyncActionFilter`; adding correlation ID to `HttpContext`; enriching response headers; short-circuiting pipeline |
| **C - Advanced** | `IResourceFilter`, `IResultFilter`; endpoint-scoped filters via `[ServiceFilter]`; `IEndpointFilter` (.NET 7+ minimal API style); custom `[Authorize]` policy handler |

**Java parallels:** `OncePerRequestFilter` / `HandlerInterceptor` → `IMiddleware` / `IActionFilter`.

---

### Lesson 07 — Error Handling & Validation
**Branches:** `lesson/07-error-handling/a-basic` · `b-intermediate` · `c-advanced`

| Part | Content |
|------|---------|
| **A - Basic** | `try/catch` in controllers; `IActionResult` error responses; `ModelState` validation; `[Required]`, `[Range]`, Data Annotations |
| **B - Intermediate** | Global exception handler with `IExceptionHandler` (.NET 8+); `ProblemDetails` (RFC 7807); `FluentValidation` integration |
| **C - Advanced** | Custom exception hierarchy (`DomainException`, `NotFoundException`); exception → HTTP status mapping; validation pipeline with MediatR; structured error logging |

**Java parallels:** `@ControllerAdvice` / `@ExceptionHandler` → `IExceptionHandler`; Bean Validation → FluentValidation / Data Annotations.

---

### Lesson 08 — Application Events & Pub/Sub
**Branches:** `lesson/08-events/a-basic` · `b-intermediate` · `c-advanced`

| Part | Content |
|------|---------|
| **A - Basic** | C# `event` keyword; delegates (`Action`, `Func`, `EventHandler<T>`); simple in-process publisher/subscriber |
| **B - Intermediate** | `MediatR` — `INotification` + `INotificationHandler<T>`; `IRequest<T>` + `IRequestHandler<T>`; decoupled domain events (e.g. `AccountCreatedEvent`) |
| **C - Advanced** | `IHostedService` as event consumer; `Channel<T>` for async in-process messaging; outbox pattern intro; comparison with Kafka/RabbitMQ integration via `MassTransit` |

**Java parallels:** Spring `ApplicationEvent` / `@EventListener` → MediatR notifications; Kafka consumer → `IHostedService` + `Channel<T>`.

---

### Lesson 09 — Scheduled Tasks
**Branches:** `lesson/09-scheduled-tasks/a-basic` · `b-intermediate`  *(no part C)*

| Part | Content |
|------|---------|
| **A - Basic** | `IHostedService` manual implementation; `BackgroundService` base class; `PeriodicTimer` (modern .NET approach); graceful shutdown with `CancellationToken` |
| **B - Intermediate** | **Quartz.NET** — `IJob`, `ITrigger`, cron expressions; scoped services inside a background job; job concurrency control; `ISchedulerFactory` DI registration |

**Java parallels:** `@Scheduled` → `BackgroundService` / Quartz.NET; `ThreadPoolTaskScheduler` → Quartz `IScheduler`.

---

### Lesson 10 — File Handling & Templating
**Branches:** `lesson/10-file-handling/a-basic` · `b-intermediate` · `c-advanced`

| Part | Content |
|------|---------|
| **A - Basic** | `File`, `Directory` static helpers; `StreamReader`/`StreamWriter`; `using` declarations; `FileStream`; reading/writing transaction exports as plain text |
| **B - Intermediate** | `IFormFile` upload endpoint (import transactions from CSV); `CsvHelper` library; `JsonSerializer` (`System.Text.Json`) read/write; async file IO with `await using` |
| **C - Advanced** | **Scriban template engine** (`ITemplateEngine` / `ScribanTemplateEngine`); `{{ variable }}` / `{{ for }}` / `{{ if }}` syntax; PascalCase→snake_case model binding; generating transaction emails, bank statements, and monthly reports from named template files; `RenderStringAsync` for inline templates |

**Java parallels:** `BufferedReader`/`BufferedWriter` → `StreamReader`/`StreamWriter`; `MultipartFile` → `IFormFile`; Thymeleaf / FreeMarker / Jinja2 → Scriban `ITemplateEngine`.

---

### Lesson 11 — Encoding & Encryption (Jasypt Equivalent)
**Branches:** `lesson/11-encryption/a-basic` · `b-intermediate` · `c-advanced`

| Part | Content |
|------|---------|
| **A - Basic** | `Convert.ToBase64String` / `FromBase64String`; `System.Security.Cryptography` — hashing with `SHA256`, `HMACSHA256`; password hashing with `BCrypt.Net` |
| **B - Intermediate** | Symmetric encryption with **AES** (`Aes.Create()`); IV + key management; encrypting sensitive config values at rest |
| **C - Advanced** | **ASP.NET Core Data Protection API** (`IDataProtectionProvider`) — the real Jasypt equivalent; key rotation; protecting tokens; asymmetric RSA signing for JWTs |

**Java parallels:** Jasypt `StandardPBEStringEncryptor` → `IDataProtectionProvider`; `MessageDigest` → `System.Security.Cryptography` hash classes.

---

### Lesson 12 — Unit Testing
**Branches:** `lesson/12-unit-testing/a-basic` · `b-intermediate` · `c-advanced`

| Part | Content |
|------|---------|
| **A - Basic** | **xUnit** — `[Fact]`, `[Theory]`, `[InlineData]`; Arrange/Act/Assert; naming conventions; testing `AccountService` in isolation |
| **B - Intermediate** | **Moq** — mocking `IAccountRepository`, `IHttpClientFactory`; `Mock<T>`, `Setup`, `Verify`; `FluentAssertions` for readable assertions |
| **C - Advanced** | Integration tests with `WebApplicationFactory<Program>`; in-memory SQLite DB for EF Core tests; `TestContainers` intro; code coverage with Coverlet |

**Java parallels:** JUnit 5 → xUnit; Mockito → Moq; `@SpringBootTest` → `WebApplicationFactory`.

---

### Lesson 13 — Authentication & Authorization (JWT)
**Branches:** `lesson/13-auth-jwt/a-basic` · `b-intermediate` · `c-advanced`

| Part | Content |
|------|---------|
| **A - Basic** | JWT bearer authentication setup; `[Authorize]`, `[AllowAnonymous]`; reading claims in controller; `HttpContext.User` |
| **B - Intermediate** | Role-based authorization (`[Authorize(Roles = "Teller")]`); claim-based policies; `IAuthorizationHandler` + `AuthorizationPolicy`; token generation endpoint |
| **C - Advanced** | Refresh tokens; token revocation; `IAuthorizationRequirement` for resource-level checks (e.g. "can only access own account"); OAuth2/OpenID Connect with Keycloak (overview) |

**Java parallels:** Spring Security filter chain → ASP.NET Core auth middleware; `@PreAuthorize` → `[Authorize(Policy = "...")]`.

---

### Lesson 14 — Caching
**Branches:** `lesson/14-caching/a-basic` · `b-intermediate` · `c-advanced`

| Part | Content |
|------|---------|
| **A - Basic** | `IMemoryCache` — `Set`, `Get`, `GetOrCreate`; cache expiry (`AbsoluteExpiration`, `SlidingExpiration`); cache invalidation on write |
| **B - Intermediate** | `IDistributedCache` interface; **Redis** with `StackExchange.Redis`; cache-aside pattern in service layer; serialization strategies |
| **C - Advanced** | Response caching middleware (`[ResponseCache]`); output caching (.NET 7+); cache stampede prevention; cache warming on startup |

**Java parallels:** Spring `@Cacheable` → `IMemoryCache` / `IDistributedCache`; Spring Cache + Redis → `IDistributedCache` + StackExchange.Redis.

---

### Lesson 15 — Structured Logging & Observability
**Branches:** `lesson/15-logging/a-basic` · `b-intermediate` · `c-advanced`

| Part | Content |
|------|---------|
| **A - Basic** | Built-in `ILogger<T>`; log levels; structured logging with message templates (`{AccountId}`); logging in middleware and services |
| **B - Intermediate** | **Serilog** — sinks (console, file, seq); enrichers; correlation ID propagation through `IHttpContextAccessor`; log scopes |
| **C - Advanced** | **OpenTelemetry** — traces, metrics, logs; `ActivitySource`; exporting to Jaeger/Zipkin; health checks (`IHealthCheck`, `/health` endpoint) |

**Java parallels:** SLF4J/Logback → `ILogger<T>` / Serilog; MDC (Mapped Diagnostic Context) → Serilog `LogContext.PushProperty`; Micrometer → OpenTelemetry .NET.

---

### Lesson 26 — Frontend Serving & Web Security
**Branches:** `lesson/26-frontend-security/a-basic` · `b-intermediate` · `c-advanced`

| Part | Content |
|------|---------|
| **A - Basic** | `UseStaticFiles` / `wwwroot`; `UseDefaultFiles`; `MapFallbackToFile` for SPA; CORS (`AddCors`, `AllowSpecificOrigins`, preflight); HTTPS redirect + HSTS |
| **B - Intermediate** | **Razor Pages** (Thymeleaf/Jinja2 equivalent): `@page`, `PageModel`, tag helpers, layout (`_Layout.cshtml`), partial views, `@inject`; `@Html.AntiForgeryToken`; rendered banking statement page |
| **C - Advanced** | Security hardening: Content-Security-Policy, X-Frame-Options, X-Content-Type-Options, Referrer-Policy headers via middleware; `HttpOnly`/`Secure`/`SameSite` cookie flags; anti-forgery tokens (`IAntiforgery`); XSS prevention with Razor's auto-encoding; rate-limiting login endpoints |

**Java parallels:** Spring MVC `addResourceHandlers` → `UseStaticFiles`; `@CrossOrigin` / `CorsRegistry` → `AddCors`; Thymeleaf layouts + fragments → Razor Pages layouts + partials; Spring Security headers → custom middleware + `IAntiforgery`; `spring-boot-starter-thymeleaf` → Razor Pages.

---

## How to Work Through a Lesson

```bash
# Check out the starting point for a lesson part
git checkout lesson/01-endpoint/a-basic

# Read the README.md at the repo root for that branch
# Study the code, run it, experiment

# When ready for the next part
git checkout lesson/01-endpoint/b-intermediate
git diff lesson/01-endpoint/a-basic..lesson/01-endpoint/b-intermediate
```

## Recommended Learning Order

For a Java/Spring Boot dev, the fastest path to productivity:

1. **01 → 02 → 03** (endpoints + config + DB) — get a working CRUD API
2. **13** (auth) — essential in banking
3. **07** (error handling) — production hygiene
4. **05** (LINQ) — the most C#-specific skill to master
5. **06** (middleware) — maps closely to your Spring knowledge
6. **12** (testing) — validate everything you've built
7. Then the rest in any order
