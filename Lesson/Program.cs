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
using Lesson.Filters;
using Lesson.Middleware;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using Lesson.Options;
using Lesson.Repositories;
using Lesson.UnitOfWork;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

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

builder.Services.AddControllers(options =>
{
    // ----- 06-B: Register action filters globally -----
    options.Filters.Add<CorrelationIdFilter>();
});
builder.Services.AddOpenApi();

// ??? 06-C Extended: Rate Limiting ????????????????????????????????????????????
// ASP.NET Core 7+ ships rate limiting middleware in-box — no extra package.
//
// Three policies demonstrate the main algorithms:
//   fixed   — N requests per window, hard reset at window boundary
//   sliding — N requests per window, window slides per sub-window
//   token   — token-bucket: replenish T tokens every P period
//
// Java parallel:
//   Resilience4j RateLimiter  /  Bucket4j  /  Spring Cloud Gateway filters
//   RateLimiterConfig.custom().limitForPeriod(10).limitRefreshPeriod(...)...
// ?????????????????????????????????????????????????????????????????????????????
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Fixed window: max 10 requests per 10-second window per IP.
    // Java parallel: Bucket4j Bandwidth.simple(10, Duration.ofSeconds(10))
    options.AddFixedWindowLimiter("fixed", o =>
    {
        o.Window              = TimeSpan.FromSeconds(10);
        o.PermitLimit         = 10;
        o.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        o.QueueLimit          = 0;
    });

    // Sliding window: smoother than fixed — avoids the boundary burst.
    // Java parallel: Bucket4j sliding-window strategy
    options.AddSlidingWindowLimiter("sliding", o =>
    {
        o.Window               = TimeSpan.FromSeconds(10);
        o.PermitLimit          = 10;
        o.SegmentsPerWindow    = 5;
        o.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        o.QueueLimit           = 0;
    });

    // Token bucket: burst-friendly; tokens refill at a steady rate.
    // Java parallel: Resilience4j RateLimiter with SemaphoreBased strategy
    options.AddTokenBucketLimiter("token", o =>
    {
        o.TokenLimit          = 20;
        o.ReplenishmentPeriod = TimeSpan.FromSeconds(5);
        o.TokensPerPeriod     = 5;
        o.AutoReplenishment   = true;
        o.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        o.QueueLimit          = 0;
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
// ResponseHeaderMiddleware wraps everything below it.
app.UseMiddleware<ResponseHeaderMiddleware>();
// RequestLoggingMiddleware logs all requests that pass through.
app.UseMiddleware<RequestLoggingMiddleware>();

// Rate limiting middleware must be placed before routing/endpoints.
// Java parallel: Filter chain order in spring.security / servlet filter chain
app.UseRateLimiter();
app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();
app.Run();
