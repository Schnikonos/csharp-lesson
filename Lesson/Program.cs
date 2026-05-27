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
// Java parallel: @Bean methods in @Configuration class, or @ComponentScan
//
// DI lifetime recap:
//   AddSingleton  → one instance for the entire app lifetime
//   AddScoped     → one instance per HTTP request  ← most common for services
//   AddTransient  → new instance every time resolved
//
// AccountService holds in-memory state, so Singleton is appropriate here.
// In Lesson 03 (EF Core) we switch to Scoped because DbContext is Scoped.
builder.Services.AddSingleton<IAccountService, AccountService>();

builder.Services.AddControllers();

// OpenAPI / Swagger — accessible at /openapi/v1.json in development
// The Scalar UI (replacement for Swagger UI in .NET 9+) is auto-mapped below.
builder.Services.AddOpenApi();

// --- Middleware Pipeline ------------------------------------------------------
var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    // Serves the OpenAPI JSON document and the interactive Scalar UI at /scalar
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
