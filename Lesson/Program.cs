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
using Lesson.ScheduledTasks;
using Lesson.UnitOfWork;
using Lesson.Templating;
using Lesson.Validators;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Quartz;

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

// ----- 08-A: Domain event bus + audit subscriber -----
builder.Services.AddSingleton<Lesson.Events.DomainEventBus>();
builder.Services.AddSingleton<Lesson.Subscribers.PaymentAuditSubscriber>();

// ----- 08-C: Channel<T> outbox queue + background consumer -----
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

// ----- 10-C: Scriban template engine -----
// Scriban is a lightweight .NET template engine with {{ }} syntax inspired by Liquid/Jinja2.
// Java parallel: Thymeleaf TemplateEngine / FreeMarker Configuration
// ITemplateEngine is a singleton: the parsed template cache is thread-safe.
builder.Services.AddSingleton<ITemplateEngine>(_ =>
    new ScribanTemplateEngine(
        Path.Combine(AppContext.BaseDirectory, "Templating", "Templates")));

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

// ----- 26-A: CORS -----
// AddCors registers the CORS middleware services.
// AllowSpecificOrigins policy mirrors Spring's @CrossOrigin / WebMvcConfigurer.addCorsMappings().
// The named policy is referenced in app.UseCors() and can be applied per-endpoint with [EnableCors].
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowSpecificOrigins", policy =>
        policy.WithOrigins("http://localhost:3000", "https://app.sandboxbank.local")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials());
});

builder.Services.AddControllers(options =>
{
    // ----- 06-B: Register action filters globally -----
    options.Filters.Add<CorrelationIdFilter>();
});

// ----- 26-B: Razor Pages -----
// AddRazorPages registers the Razor Pages runtime, view engine, tag helpers, and anti-forgery.
// Java parallel: @EnableWebMvc + Thymeleaf auto-configuration
builder.Services.AddRazorPages();

builder.Services.AddOpenApi();

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

// ----- 07-B: Global exception handler (must come early so it catches all unhandled errors) -----
app.UseExceptionHandler();

app.UseHttpsRedirection();

// ----- 26-A: HSTS (HTTP Strict Transport Security) -----
// Tells browsers to always use HTTPS for this domain for the specified duration.
// Java parallel: Spring Security http.headers().httpStrictTransportSecurity()
// Only enable in production — development certificates are not trusted.
if (!app.Environment.IsDevelopment())
    app.UseHsts();

// ----- 26-A: CORS -----
// Must come BEFORE UseStaticFiles / MapControllers so preflight OPTIONS requests are handled.
// Java parallel: Spring Security CorsFilter / WebMvcConfigurer.addCorsMappings()
app.UseCors("AllowSpecificOrigins");

// ----- 26-A: Static files (wwwroot) -----
// UseDefaultFiles rewrites "/" ? "/index.html" (must come BEFORE UseStaticFiles).
// UseStaticFiles serves wwwroot/** with correct Content-Type and cache headers.
// Java parallel: spring.web.resources.static-locations + ResourceHttpRequestHandler
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthorization();
app.MapControllers();
// ----- 26-B: map Razor Pages routes (/Statement, etc.) -----
app.MapRazorPages();

// ----- 26-A: SPA fallback -----
// Any route not matched by a controller falls back to index.html so the SPA router takes over.
// Java parallel: SpaFallbackRequestMatcher / @RequestMapping("/{path:[a-z].*}")
// Must be registered AFTER MapControllers so API routes are matched first.
app.MapFallbackToFile("index.html");

app.Run();
