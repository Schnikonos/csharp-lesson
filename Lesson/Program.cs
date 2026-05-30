// =============================================================================
// LESSON 02-A: Configuration & App Customization — Basic
//
// ASP.NET Core loads configuration automatically in this order (later wins):
//   1. appsettings.json
//   2. appsettings.{Environment}.json  (e.g. appsettings.Development.json)
//   3. User Secrets          (Development only — Lesson 02-C)
//   4. Environment variables
//   5. Command-line arguments
//
// The active environment is controlled by the ASPNETCORE_ENVIRONMENT variable.
// In launchSettings.json it is pre-set to "Development" for local runs.
//
// IConfiguration is registered by the framework automatically — no explicit
// registration needed. Inject it wherever you need config values.
//
// Java parallel:
//   SpringApplication auto-loads application.properties / application.yml.
//   WebApplication.CreateBuilder() is the equivalent entry point.
// =============================================================================

var builder = WebApplication.CreateBuilder(args);

// IConfiguration is already available as builder.Configuration at this point.
// You can read values here for early startup decisions, e.g.:
//   var bankName = builder.Configuration["Bank:Name"];

builder.Services.AddControllers();
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();
app.Run();
