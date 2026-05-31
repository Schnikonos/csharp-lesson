// =============================================================================
// LESSON 03-B: Repository Pattern, IQueryable vs IEnumerable, Owned Entities
// (builds on 03-A EF Core baseline)
//
// New registrations:
//   IAccountRepository ? AccountRepository (Scoped)
// =============================================================================
// LESSON 03-A: EF Core CRUD — DbContext, SQLite, Migrations
//
// Entity Framework Core is .NET's official ORM.
//
// Key concepts:
//   DbContext     — Unit of Work + identity map; tracks entity changes in memory.
//                   Java parallel: EntityManager / @PersistenceContext
//   DbSet<T>      — Repository for a specific entity type.
//                   Java parallel: JpaRepository<T, ID>
//   Migrations    — Code-first schema versioning.
//                   Java parallel: Flyway / Liquibase
//   SaveChangesAsync() — Flushes all tracked changes to the DB in a transaction.
//                        Java parallel: entityManager.flush() / @Transactional
//
// Lifetime:
//   DbContext is registered as Scoped (one per HTTP request) — the EF Core default.
//   Never inject a Scoped DbContext into a Singleton service; use IDbContextFactory<T> instead.
// =============================================================================

using Lesson.Configuration;
using Lesson.Data;
using Lesson.ExceptionHandlers;
using Lesson.Filters;
using Lesson.HostedServices;
using Lesson.Jobs;
using Lesson.Messaging;
using Lesson.Middleware;
using Lesson.Options;
using Lesson.Pipeline;
using Lesson.Repositories;
using Microsoft.AspNetCore.Authorization;
using Lesson.Controllers;
using Lesson.ScheduledTasks;
using Lesson.UnitOfWork;
using Lesson.Validators;
using FluentValidation;
using MassTransit;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Quartz;
using Serilog;
using Serilog.Context;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;
using Lesson.MinimalApi;

var builder = WebApplication.CreateBuilder(args);

// ----- 15-B: Serilog -----
// UseSerilog replaces the built-in Microsoft.Extensions.Logging pipeline.
// ReadFrom.Configuration picks up the "Serilog" section from appsettings.json:
//   - MinimumLevel, WriteTo (Console/File/Seq), Enrich.FromLogContext
// Enrich.FromLogContext picks up properties pushed via LogContext.PushProperty (correlation ID).
// Java parallel: Logback + logback.xml configured via application.properties
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .CreateLogger();

builder.Host.UseSerilog();

// ----- Options carried from Lesson 02 -----
builder.Services
    .AddOptions<BankOptions>()
    .BindConfiguration(BankOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddOptions<FeatureFlagOptions>()
    .BindConfiguration(FeatureFlagOptions.SectionName);

builder.Services
    .AddOptions<ConnectionStringOptions>()
    .BindConfiguration(ConnectionStringOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddOptions<TransferLimitOptions>("domestic")
    .BindConfiguration("TransferLimits:Domestic")
    .ValidateDataAnnotations();

builder.Services
    .AddOptions<TransferLimitOptions>("international")
    .BindConfiguration("TransferLimits:International")
    .ValidateDataAnnotations();

((IConfigurationBuilder)builder.Configuration).Add(new InMemoryDbConfigurationSource
{
    Data = new Dictionary<string, string?>
    {
        ["CustomConfig:WelcomeMessage"] = "Hello from the custom provider!",
        ["CustomConfig:MaxRetries"] = "3",
        ["CustomConfig:ServiceUrl"] = "https://api.acmebank.internal"
    }
});

// ----- 03-A: EF Core — SQLite DbContext registration -----
// AddDbContext registers BankingDbContext as Scoped.
// The connection string is read from appsettings.json "ConnectionStrings:BankDb".
// In Development, this is also overridable via User Secrets (Lesson 02-C).
builder.Services.AddDbContext<BankingDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("BankDb")
        ?? "Data Source=bank.db"));

// ----- 03-B: Repository registration -----
builder.Services.AddScoped<IAccountRepository, AccountRepository>();

// ----- 03-C: Unit of Work registration -----
// UnitOfWork is Scoped so it shares the same DbContext instance as AccountRepository.
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();

// ----- 04-A: Customer repository -----
builder.Services.AddScoped<ICustomerRepository, CustomerRepository>();

// ----- 05: LINQ service (in-memory, no DB) -----
builder.Services.AddSingleton<Lesson.Services.LinqService>();
builder.Services.AddSingleton<Lesson.Services.LinqIntermediateService>();
builder.Services.AddSingleton<Lesson.Services.LinqAdvancedService>();

// ----- 06: Middleware (IMiddleware — lifetime managed by DI) -----
builder.Services.AddTransient<RequestLoggingMiddleware>();
builder.Services.AddTransient<ResponseHeaderMiddleware>();

// ----- 07-B + 07-C: Exception handlers (specific FIRST, catch-all LAST) -----
// IExceptionHandler pipeline checks handlers in registration order.
builder.Services.AddExceptionHandler<DomainExceptionHandler>();
builder.Services.AddExceptionHandler<ValidationExceptionHandler>();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddValidatorsFromAssemblyContaining<CreateTransferRequestValidator>();

// ----- 07-C: MediatR + validation pipeline behaviour -----
builder.Services.AddMediatR(cfg =>
    cfg.RegisterServicesFromAssemblyContaining<Program>());
builder.Services.AddTransient(
    typeof(IPipelineBehavior<,>),
    typeof(ValidationBehavior<,>));

// ----- 18-B: Logging pipeline behaviour -----
// LoggingBehavior wraps every handler and logs the request name and elapsed time.
// Registered AFTER ValidationBehavior so execution order is:
//   LoggingBehavior (outermost) ? ValidationBehavior ? Handler
// Java parallel: Spring AOP @Around advice ordering via @Order
builder.Services.AddTransient(
    typeof(IPipelineBehavior<,>),
    typeof(LoggingBehavior<,>));
// TransactionBehavior — inner; only fires for ICommand<T> requests
builder.Services.AddTransient(
    typeof(IPipelineBehavior<,>),
    typeof(TransactionBehavior<,>));

// ----- 19-B: DDD aggregate repository -----
// IAggregateRepository abstracts EF Core from the domain layer.
// Java parallel: Spring Data @Repository / Axon EventSourcingRepository
builder.Services.AddScoped<Lesson.Domain.IAggregateRepository, Lesson.Infrastructure.EfAggregateRepository>();

// ----- 19-C: AggregateUnitOfWork — post-commit domain event dispatch -----
// AggregateUnitOfWork wraps SaveChangesAsync + domain-event dispatch in one step.
// Translates DbUpdateConcurrencyException into DomainConcurrencyException (no EF leak).
// Java parallel: @TransactionalEventListener(phase = AFTER_COMMIT)
builder.Services.AddScoped<Lesson.Infrastructure.AggregateUnitOfWork>();

// ----- 08-A + audit subscriber -----
builder.Services.AddSingleton<Lesson.Events.DomainEventBus>();
builder.Services.AddSingleton<Lesson.Subscribers.PaymentAuditSubscriber>();

// ----- 11-C: Data Protection API -----
// AddDataProtection sets up the key ring (stored in %APPDATA%\ASP.NET\DataProtection-Keys by default).
// In production: .PersistKeysToAzureBlobStorage(...) + .ProtectKeysWithAzureKeyVault(...)
builder.Services.AddDataProtection();

// ----- 13-A: JWT Authentication -----
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
var jwtOpts = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme    = Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
    {
        ValidateIssuer           = true,
        ValidateAudience         = true,
        ValidateLifetime         = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer              = jwtOpts.Issuer,
        ValidAudience            = jwtOpts.Audience,
        IssuerSigningKey         = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(
                                       System.Text.Encoding.UTF8.GetBytes(jwtOpts.SecretKey)),
    };
});
builder.Services.AddAuthorization();

// ----- 13-B: Role/claim-based authorization -----
builder.Services.AddAuthorization(options =>
    options.AddPolicy("AccountOwner", policy =>
        policy.Requirements.Add(new AccountOwnerRequirement())));
builder.Services.AddScoped<IAuthorizationHandler, AccountOwnerHandler>();

// ----- 13-C: Refresh token store (Singleton — lives for app lifetime) -----
builder.Services.AddSingleton<TokenStore>();

// ----- 14-A: IMemoryCache -----
// AddMemoryCache registers IMemoryCache as a Singleton.
// Java parallel: @EnableCaching + Spring CacheManager
builder.Services.AddMemoryCache();

// ----- 14-B: IDistributedCache -----
// In Development: in-process memory (single-node, no Redis required).
// In Production:  swap to AddStackExchangeRedisCache — the interface stays the same.
// Java parallel: Spring RedisCacheManager / CaffeineCacheManager — swap via @Bean
if (builder.Environment.IsDevelopment())
    builder.Services.AddDistributedMemoryCache();
// Uncomment for real Redis:
// builder.Services.AddStackExchangeRedisCache(o =>
//     o.Configuration = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379");

// ----- 14-C: Output caching (.NET 7+) -----
// AddOutputCache registers the output cache service and its policy registry.
// Java parallel: @EnableCaching + CaffeineCacheManager (server-side response cache)
builder.Services.AddOutputCache(opts =>
{
    // "Lock" policy — serialises concurrent requests for the same cache key,
    // preventing cache stampedes (dog-piling).
    opts.AddPolicy("Lock", pb => pb.SetLocking(true));
});
builder.Services.AddResponseCaching();      // needed for [ResponseCache] HTTP headers
builder.Services.AddSingleton<OutboxChannel>();
builder.Services.AddSingleton<OutboxConsumerService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<OutboxConsumerService>());

// ----- 09-A: PeriodicTimer-based scheduled job -----
builder.Services.AddSingleton<JobHistoryStore>();
builder.Services.AddSingleton<InterestCalculationService>(sp =>
    new InterestCalculationService(
        sp.GetRequiredService<JobHistoryStore>(),
        sp.GetRequiredService<ILogger<InterestCalculationService>>(),
        period: TimeSpan.FromSeconds(30)));   // 30-second interval in production host
builder.Services.AddHostedService(sp => sp.GetRequiredService<InterestCalculationService>());

// ----- 09-B: Quartz.NET scheduler -----
// AddQuartz registers the scheduler engine and wires IJob resolution to DI.
// Java parallel: spring-boot-starter-quartz + @EnableScheduling
builder.Services.AddQuartz(q =>
{
    // Register the job with its key
    q.AddJob<StatementGenerationJob>(opts => opts.WithIdentity(StatementGenerationJob.Key));

    // Add a trigger: cron "every minute" for demo purposes
    // (in production this would be e.g. "0 0 2 * * ?" for 2 AM daily)
    q.AddTrigger(opts => opts
        .ForJob(StatementGenerationJob.Key)
        .WithIdentity("StatementTrigger", "Banking")
        .WithCronSchedule("0 * * * * ?")     // every minute at second 0
        .StartNow());
});
// AddQuartzHostedService starts the scheduler as an IHostedService
builder.Services.AddQuartzHostedService(opts =>
    opts.WaitForJobsToComplete = true);      // graceful shutdown waits for running jobs

// ----- 20-A: gRPC — adds HTTP/2 + Protobuf service infrastructure -----
// Java parallel: @EnableGrpc / spring-grpc starter
builder.Services.AddGrpc();

builder.Services.AddControllers(options =>
{
    // ----- 06-B: Register action filters globally -----
    options.Filters.Add<CorrelationIdFilter>();
});
// ----- 15-B: IHttpContextAccessor — needed by SerilogDemoController -----
// AddHttpContextAccessor registers IHttpContextAccessor as a Singleton.
// Java parallel: @RequestScope beans / HttpServletRequest injection
builder.Services.AddHttpContextAccessor();

// ----- 15-C: OpenTelemetry — traces + metrics -----
// AddOpenTelemetry wires the OTel SDK into the ASP.NET Core host.
//   WithTracing   — captures distributed traces (spans) for HTTP and EF Core.
//   WithMetrics   — captures process / HTTP / runtime metrics.
// Exporter: Console is used here for local visibility; swap to OTLP for Jaeger/Zipkin.
// Java parallel: Micrometer + Brave/OpenTelemetry SDK; io.opentelemetry:opentelemetry-sdk
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("BankingApi"))
    .WithTracing(tracing => tracing
        .AddSource("BankingApi")                    // OtelDemoController custom spans
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()         // spans for outbound HttpClient calls
        .AddEntityFrameworkCoreInstrumentation()// spans for EF Core DB commands
        .AddConsoleExporter())                  // prints spans to the console
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()         // http.server.* metrics
        .AddHttpClientInstrumentation()         // http.client.* metrics
        .AddConsoleExporter());

// ----- 15-C: Health checks -----
// AddHealthChecks returns a builder that lets you chain specific checks.
// Java parallel: Spring Boot Actuator /actuator/health + HealthIndicator
builder.Services.AddHealthChecks()
    .AddDbContextCheck<BankingDbContext>("database"); // pings the DB via EF Core

builder.Services.AddOpenApi();

// ----- 17-A: MassTransit — in-memory transport -----
// MassTransit is an open-source message-bus abstraction for .NET.
// UsingInMemory() wires a fully functional in-process bus — no broker required.
// Swap to UsingRabbitMq() / UsingKafka() without changing consumers or publishers.
//
// Java parallel:
//   Spring ApplicationEventPublisher (in-process)  ?  IPublishEndpoint (in-memory)
//   @RabbitListener                                ?  IConsumer<T>
//   RabbitTemplate.convertAndSend()               ?  IPublishEndpoint.Publish()
builder.Services.AddMassTransit(x =>
{
    // Register all consumers in this assembly automatically
    x.AddConsumers(typeof(Program).Assembly);

    // 17-C: Register the transfer saga with in-memory repository
    x.AddSagaStateMachine<
        Lesson.Messaging.Sagas.TransferStateMachine,
        Lesson.Messaging.Sagas.TransferSagaState>()
        .InMemoryRepository();

    x.UsingInMemory((ctx, cfg) =>
    {
        cfg.ConfigureEndpoints(ctx);
    });
});

var app = builder.Build();

// ----- Apply pending EF Core migrations automatically at startup -----
// In production you would use a dedicated migration step (e.g. CI/CD pipeline).
// For learning purposes, MigrateAsync() is called here so the DB is always up to date.
// Java parallel: Flyway.migrate() / spring.flyway.enabled=true
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<BankingDbContext>();
    await db.Database.MigrateAsync();
}

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

// ----- 06-C: Minimal-API endpoint protected by IEndpointFilter -----
app.MapGet("/minimal/secure", () => Results.Ok(new { secret = "you have the key!" }))
   .AddEndpointFilter(new ApiKeyEndpointFilter("lesson06"));

// ----- 06-A: Middleware pipeline — ORDER MATTERS -----
app.UseMiddleware<ResponseHeaderMiddleware>();
// ----- 15-B: Serilog request logging -----
// UseSerilogRequestLogging replaces the verbose ASP.NET hosting request logs with a
// single structured entry per request (method, path, status, elapsed).
// It must come BEFORE UseRouting/auth/controllers to capture the full request.
// Java parallel: logback-access / spring-boot-starter-actuator access-log
app.UseSerilogRequestLogging();
app.UseMiddleware<RequestLoggingMiddleware>();

// ----- 07-B: Global exception handler (must come early so it catches all unhandled errors) -----
app.UseExceptionHandler();

app.UseHttpsRedirection();
// ----- 13-A: Auth middleware must come AFTER exception handler, BEFORE MapControllers -----
app.UseAuthentication();
// ----- 14-C: Output/response caching middleware -----
app.UseResponseCaching();
app.UseOutputCache();
app.UseAuthorization();
app.MapControllers();
// ----- 20-A: gRPC endpoint — served on HTTP/2 alongside REST (HTTP/1.1) -----
// Java parallel: @GrpcService class registration picked up by spring-grpc
app.MapGrpcService<Lesson.Services.GrpcBankingService>();
// ----- 21-A: Minimal API endpoints -----
// Routes defined via extension method; no controller class needed.
// Java parallel: @GetMapping / @PostMapping lambdas in Spring functional routing
app.MapMinimalAccounts();
// ----- 15-C: Health check endpoint -----
// /health — returns 200 Healthy / 503 Unhealthy + JSON payload.
// Java parallel: GET /actuator/health
app.MapHealthChecks("/health");
app.Run();
