using Lesson.Config;
using Lesson.Services;

// -----------------------------------------------------------------------------
// C# NOTE: Program.cs is the application entry point — the equivalent of
// Spring Boot's @SpringBootApplication main class + application context setup.
//
// ASP.NET Core uses a "minimal hosting model" (introduced in .NET 6):
//   1. WebApplication.CreateBuilder(args)  — configure services (DI container)
//   2. builder.Build()                     — build the app
//   3. app.Use* / app.Map*                 — configure the middleware pipeline
//   4. app.Run()                           — start the server
//
// Java parallel:
//   SpringApplication.run(...)  →  WebApplication.CreateBuilder + app.Run()
//   application.properties      →  appsettings.json  (Lesson 02)
// -----------------------------------------------------------------------------

var builder = WebApplication.CreateBuilder(args);

// --- Service Registration (DI Container) -------------------------------------

// AccountService holds in-memory state → Singleton (one instance for app lifetime).
builder.Services.AddSingleton<IAccountService, AccountService>();

// -----------------------------------------------------------------------------
// OPTIONS PATTERN — Strongly-typed configuration
//
// Configure<T> binds the named JSON section from appsettings.json to the POCO.
// Once registered, IOptions<ExchangeRateOptions> can be injected anywhere.
//
// Java parallel:
//   @EnableConfigurationProperties(ExchangeRateProperties.class)
//   + @ConfigurationProperties(prefix = "exchange-rate")
//   → builder.Services.Configure<ExchangeRateOptions>(...)
// -----------------------------------------------------------------------------
builder.Services.Configure<ExchangeRateOptions>(
    builder.Configuration.GetSection(ExchangeRateOptions.SectionName));

// -----------------------------------------------------------------------------
// TYPED HTTP CLIENT REGISTRATION
//
// AddHttpClient<TClient, TImplementation>() does three things:
//   1. Registers ExchangeRateService in the DI container (Transient by default)
//   2. Creates a managed HttpClient via IHttpClientFactory and injects it
//   3. Handles HttpMessageHandler lifetime & pooling — no socket exhaustion
//
// Java parallel:
//   @Bean WebClient myWebClient(WebClient.Builder builder) { ... }
//   → builder.Services.AddHttpClient<IExchangeRateService, ExchangeRateService>()
// -----------------------------------------------------------------------------
builder.Services.AddHttpClient<IExchangeRateService, ExchangeRateService>();

builder.Services.AddControllers();

// OpenAPI / Swagger — accessible at /openapi/v1.json in development
builder.Services.AddOpenApi();

// --- Middleware Pipeline ------------------------------------------------------
var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
