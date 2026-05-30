using Lesson.Config;
using Lesson.Services;
using Microsoft.Extensions.Http.Resilience;
using Polly;

// -----------------------------------------------------------------------------
// C# NOTE: Program.cs is the application entry point — the equivalent of
// Spring Boot's @SpringBootApplication main class + application context setup.
// -----------------------------------------------------------------------------

var builder = WebApplication.CreateBuilder(args);

// --- Service Registration (DI Container) -------------------------------------

builder.Services.AddSingleton<IAccountService, AccountService>();

// Bind strongly-typed configuration (see Lesson 01-B for in-depth notes)
builder.Services.Configure<ExchangeRateOptions>(
    builder.Configuration.GetSection(ExchangeRateOptions.SectionName));

// ─── Typed HttpClient with Resilience Pipeline ────────────────────────────────
//
// NEW IN LESSON 01-C: instead of configuring the HttpClient inside the service
// constructor, we do it here with the fluent builder. The service class is now
// only responsible for making calls — not for its own configuration.
//
// Java parallel:
//   @Bean WebClient myClient() {
//       return WebClient.builder().baseUrl("...").filter(retryFilter).build();
//   }

var exchangeRateOptions = builder.Configuration
    .GetSection(ExchangeRateOptions.SectionName)
    .Get<ExchangeRateOptions>()!;

builder.Services
    .AddHttpClient<IExchangeRateService, ExchangeRateService>(client =>
    {
        // BaseAddress and Timeout were previously set in the service constructor.
        // Moving them here keeps the service focused on business logic only.
        client.BaseAddress = new Uri(exchangeRateOptions.BaseUrl);
        client.Timeout     = TimeSpan.FromSeconds(exchangeRateOptions.TimeoutSeconds);
    })
    // ─── Standard Resilience Pipeline ─────────────────────────────────────────
    //
    // AddStandardResilienceHandler() wires five Polly v8 strategies in sequence:
    //
    //   RateLimiter → TotalTimeout → Retry → CircuitBreaker → AttemptTimeout
    //
    //  • RateLimiter    — caps concurrent requests from this client
    //  • TotalTimeout   — hard ceiling including all retry attempts
    //  • Retry          — exponential back-off + jitter on transient errors (5xx, 408, 429)
    //  • CircuitBreaker — opens after a failure-rate threshold; fails fast while open
    //  • AttemptTimeout — per-attempt timeout (shorter than total)
    //
    // Java parallel:
    //   Resilience4j decorators: @Retry + @CircuitBreaker + @TimeLimiter
    //   RetryRegistry / CircuitBreakerRegistry configured as @Bean
    .AddStandardResilienceHandler(options =>
    {
        // Retry: up to 3 attempts on transient HTTP errors.
        // Exponential back-off with jitter prevents "thundering herd" when many
        // clients retry the same failing upstream simultaneously.
        options.Retry.MaxRetryAttempts = 3;
        options.Retry.BackoffType      = DelayBackoffType.Exponential;
        options.Retry.UseJitter        = true;
        options.Retry.Delay            = TimeSpan.FromMilliseconds(200);

        // Circuit Breaker: open when ≥50% of requests fail in a 30-second
        // sliding window (minimum 5 requests sampled).
        // Stays open for 15 seconds before allowing one probe request through.
        options.CircuitBreaker.FailureRatio      = 0.5;
        options.CircuitBreaker.SamplingDuration  = TimeSpan.FromSeconds(30);
        options.CircuitBreaker.MinimumThroughput = 5;
        options.CircuitBreaker.BreakDuration     = TimeSpan.FromSeconds(15);

        // Per-attempt and total timeouts.
        options.AttemptTimeout.Timeout      = TimeSpan.FromSeconds(5);
        options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(30);
    });

// ─── Output Caching ───────────────────────────────────────────────────────────
//
// Output caching stores the full serialized HTTP response. On a cache hit the
// controller is NEVER invoked — ASP.NET Core returns the stored bytes directly.
// This is the built-in output cache introduced in .NET 7 (no extra package).
//
// We register a named policy "ExchangeRates" with a 60-second expiry.
// The cache key is derived from the request URL, so each currency is cached
// separately (/exchangerate/EUR ≠ /exchangerate/USD).
//
// Java parallel:
//   @EnableCaching + CaffeineCacheManager bean
//   + @Cacheable(value = "exchangeRates", key = "#base") on service/controller
builder.Services.AddOutputCache(options =>
    options.AddPolicy("ExchangeRates", policy =>
        policy.Expire(TimeSpan.FromSeconds(60))));

builder.Services.AddControllers();
builder.Services.AddOpenApi();

// --- Middleware Pipeline ------------------------------------------------------
var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseHttpsRedirection();

// UseOutputCache MUST be placed BEFORE MapControllers so it can intercept
// and short-circuit requests that have a cached response.
app.UseOutputCache();

app.UseAuthorization();
app.MapControllers();
app.Run();
