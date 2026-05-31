# C# Learning Curriculum — Senior Dev Track (Banking Context)

> **Target audience:** Experienced Java/Spring Boot and C++ developer preparing for a senior C# role in a banking environment.
>
> **Domain used throughout all lessons:** Banking — accounts, customers, transactions.
>
> **Project:** ASP.NET Core 10 Web API (`Lesson/`)

---

## Standing Instruction — When Building a Lesson

> **Every lesson implementation MUST include a companion test project / test class** covering the concepts introduced in that lesson.
> The test type should match the lesson topic:
>
> | Lesson topic | Expected test type |
> |---|---|
> | Service / domain logic | Unit test (xUnit + Moq) |
> | Controller / endpoint | Integration test (`WebApplicationFactory`) |
> | Middleware / filters | Integration test with custom `TestServer` |
> | EF Core / repository | In-memory SQLite or TestContainers |
> | Background services | Unit test with `CancellationTokenSource` |
> | Encryption / encoding | Pure unit tests (no mocks needed) |
>
> Tests must be committed on the same branch as the lesson code.
> This ensures every lesson also teaches the **unittest concept** most naturally associated with it.

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

### Lesson 10 — File Handling
**Branches:** `lesson/10-file-handling/a-basic` · `b-intermediate`  *(no part C)*

| Part | Content |
|------|---------|
| **A - Basic** | `File`, `Directory` static helpers; `StreamReader`/`StreamWriter`; `using` declarations; `FileStream`; reading/writing transaction exports as plain text |
| **B - Intermediate** | `IFormFile` upload endpoint (import transactions from CSV); `CsvHelper` library; `JsonSerializer` (`System.Text.Json`) read/write; async file IO with `await using` |

**Java parallels:** `BufferedReader`/`BufferedWriter` → `StreamReader`/`StreamWriter`; `MultipartFile` → `IFormFile`.

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

### Lesson 16 — Multithreading & Concurrency
**Branches:** `lesson/16-multithreading/a-basic` · `b-intermediate` · `c-advanced`

| Part | Content |
|------|--------|
| **A - Basic** | `Thread`, `Task`, `Task.Run`; `async`/`await` deep dive (state machine, `ConfigureAwait`); `Task.WhenAll` / `Task.WhenAny`; `CancellationToken` patterns |
| **B - Intermediate** | Thread-safety primitives: `lock`, `Monitor`, `Interlocked`, `SemaphoreSlim`; `ConcurrentDictionary` / `ConcurrentQueue`; `Parallel.ForEach` / `Parallel.For`; `ThreadLocal<T>` |
| **C - Advanced** | `Channel<T>` for producer/consumer pipelines; `IAsyncEnumerable<T>` streaming; `ValueTask`; thread pool tuning; deadlock diagnosis; `Mutex` / `ReaderWriterLockSlim` for cross-process scenarios |

**Java parallels:** `CompletableFuture` → `Task`; `synchronized` / `ReentrantLock` → `lock` / `SemaphoreSlim`; `ConcurrentHashMap` → `ConcurrentDictionary`; `ExecutorService` → `Task` + thread pool; `BlockingQueue` → `Channel<T>`.

**Unit tests for this lesson:** `Task`-based unit tests using `async Task` test methods; testing cancellation with `CancellationTokenSource`; verifying thread-safe behaviour under concurrent load with `Parallel.For`.

---

---

### Lesson 17 — Messaging with MassTransit (RabbitMQ / Kafka)
**Branches:** `lesson/17-messaging/a-basic` · `b-intermediate` · `c-advanced`

| Part | Content |
|------|---------|
| **A - Basic** | MassTransit in-memory transport; `IPublishEndpoint`; `IConsumer<T>`; sending `AccountCreatedEvent` from the accounts endpoint; consumer registered via `AddConsumer<T>` |
| **B - Intermediate** | RabbitMQ transport (test with `MassTransit.TestFramework`); request/response pattern (`IRequestClient<T>`); message retry and error queues; `IConsumeContext` headers |
| **C - Advanced** | Outbox pattern (`UseEntityFrameworkOutbox`); Saga / `MassTransitStateMachine` for a multi-step transfer workflow; Kafka transport overview; consumer fault handling |

**Java parallels:** Spring AMQP `@RabbitListener` → `IConsumer<T>`; `RabbitTemplate.send` → `IPublishEndpoint.Publish`; Debezium outbox → MassTransit EF Core outbox.

---

### Lesson 18 — CQRS + MediatR Pipeline Behaviours
**Branches:** `lesson/18-cqrs-mediatr/a-basic` · `b-intermediate` · `c-advanced`

| Part | Content |
|------|---------|
| **A - Basic** | CQRS split: `ICommand<T>` / `IQuery<T>` marker interfaces; `IRequestHandler<TRequest, TResponse>`; separate `Commands/` and `Queries/` folders; dispatch from controller via `ISender` |
| **B - Intermediate** | `IPipelineBehavior<TRequest, TResponse>` — logging behaviour, validation behaviour (FluentValidation), transaction behaviour (wrapping command in `SaveChangesAsync`); behaviour ordering |
| **C - Advanced** | Read model projection (query handler returns a DTO from a read-optimised query, separate from the write model); `INotification` domain events raised inside aggregate, dispatched post-commit; `MediatR` + OpenTelemetry tracing per handler |

**Java parallels:** Spring `@CommandHandler` / Axon Framework → MediatR handlers; Spring AOP `@Around` → `IPipelineBehavior`.

---

### Lesson 19 — Domain-Driven Design (DDD) Building Blocks
**Branches:** `lesson/19-ddd/a-basic` · `b-intermediate` · `c-advanced`

| Part | Content |
|------|---------|
| **A - Basic** | Aggregate root base class (`AggregateRoot`); value objects with `record struct`; domain events raised inside aggregate (`AddDomainEvent`); entity vs value object distinction |
| **B - Intermediate** | Repository abstraction over EF Core aggregate root; domain service vs application service; anti-corruption layer (ACL) adapter for external exchange-rate service; `DomainException` hierarchy |
| **C - Advanced** | Bounded context mapping; `IUnitOfWork` dispatching domain events after `SaveChangesAsync`; eventual consistency between bounded contexts via MediatR notifications; aggregate versioning (optimistic concurrency) |

**Java parallels:** Axon `@Aggregate` → `AggregateRoot`; Spring `@DomainEvents` → `AddDomainEvent`; Hexagonal ports/adapters → ACL / repository abstractions.

---

### Lesson 20 — gRPC
**Branches:** `lesson/20-grpc/a-basic` · `b-intermediate`

| Part | Content |
|------|---------|
| **A - Basic** | `.proto` contract; `Grpc.AspNetCore` service registration; unary RPC; `MapGrpcService<T>`; proto → C# code generation; calling the service with `GrpcChannel` in tests |
| **B - Intermediate** | Server-streaming RPC returning `IAsyncEnumerable`; client-streaming; deadline / cancellation propagation; Bearer token auth via `CallCredentials`; gRPC reflection for debugging |

**Java parallels:** `io.grpc` / Spring gRPC → `Grpc.AspNetCore`; `StreamObserver` → `IServerStreamWriter<T>`.

---

### Lesson 21 — Minimal API & API Versioning
**Branches:** `lesson/21-minimal-api/a-basic` · `b-intermediate`

| Part | Content |
|------|---------|
| **A - Basic** | Minimal API `app.MapGet/Post/Put/Delete`; `IEndpointRouteBuilder` extension method groups; `IEndpointFilter` for validation; typed result helpers (`TypedResults.Ok`, `TypedResults.NotFound`) |
| **B - Intermediate** | `Asp.Versioning` — URL-segment versioning (`/v1/accounts`); header versioning; deprecation; per-version Swagger UI with built-in `.NET 9 OpenAPI`; side-by-side controller vs minimal API |

**Java parallels:** Spring `@RequestMapping` → `app.MapGet`; `@RestControllerAdvice` filter → `IEndpointFilter`.

---

### Lesson 22 — Result Pattern & Functional Error Handling
**Branches:** `lesson/22-result-pattern/a-basic` · `b-intermediate`

| Part | Content |
|------|---------|
| **A - Basic** | `Result<T>` / `Result` types with `ErrorOr` library; replacing thrown exceptions with `Error` returns in domain / service layer; pattern-match (`switch`) on result; mapping `Error` to `ProblemDetails` in controller |
| **B - Intermediate** | Railway-oriented pipeline: chain `.Then()` / `.Map()` / `.Match()`; `IActionResult` extension to auto-map `ErrorOr<T>` to HTTP responses; FluentValidation integration returning `Error.Validation` |

**Java parallels:** Vavr `Either<Error, T>` / `Try<T>` → `ErrorOr<T>`; `.map()` / `.getOrElse()` → `.Map()` / `.Match()`.

---

### Lesson 23 — Docker & docker-compose
**Branches:** `lesson/23-docker/a-basic`  *(single part)*

| Part | Content |
|------|---------|
| **A - Basic** | Multi-stage `Dockerfile` for the ASP.NET Core app; `docker-compose.yml` with app + PostgreSQL + Redis + RabbitMQ; environment variable injection; `.dockerignore`; health-check `HEALTHCHECK` directive in Dockerfile; `docker compose up` walkthrough |

**Java parallels:** Maven `spring-boot:build-image` → `dotnet publish` with `--os linux`; Spring Boot Docker Compose support → same pattern in .NET 8+.

---

### Lesson 24 — SignalR (Real-time)
**Branches:** `lesson/24-signalr/a-basic` · `b-intermediate`

| Part | Content |
|------|---------|
| **A - Basic** | `Hub` with typed client interface; `IHubContext<T>` injection into controllers; broadcasting balance-change notifications; connecting from a test client using `HubConnection` (SignalR .NET client) |
| **B - Intermediate** | Groups + user-to-connection mapping; authorization on hub methods (`[Authorize]`); scaling with Redis backplane (`AddStackExchangeRedis`); reconnection strategies; `IUserIdProvider` |

**Java parallels:** Spring WebSocket `@MessageMapping` / STOMP → SignalR `Hub`; `SimpMessagingTemplate` → `IHubContext<T>`.

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
6. **16** (multithreading) — `async`/`await` is everywhere in C#; learn it early
7. **12** (testing) — validate everything you've built
8. Then the rest in any order

> **Already covered — no new lessons needed:**
> - Logging → **Lesson 15** (log levels, Serilog sinks/file rotation/enrichers, OpenTelemetry)
> - Caching → **Lesson 14** (IMemoryCache, Redis, output caching)
