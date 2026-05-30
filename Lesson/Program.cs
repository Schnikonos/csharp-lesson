// =============================================================================
// LESSON 02-B: Configuration — Intermediate (Strongly-Typed Options)
//
// Instead of injecting IConfiguration and reading string keys, ASP.NET Core
// provides the Options pattern: bind a config section to a typed POCO.
//
// Three interfaces, three lifetimes:
//   IOptions<T>          — singleton; value is frozen at startup; cheapest.
//   IOptionsSnapshot<T>  — scoped (per HTTP request); reflects file changes
//                          within the same request scope; needs reloadOnChange.
//   IOptionsMonitor<T>   — singleton; always returns the latest value;
//                          provides a change callback.
//
// Validation:
//   .ValidateDataAnnotations() — validate [Required], [Range], etc. at startup
//   .ValidateOnStart()         — fail fast: throw at app launch, not first use
//
// Java parallel:
//   @ConfigurationProperties(prefix="bank") + @Validated in Spring Boot.
// =============================================================================

using Lesson.Options;

var builder = WebApplication.CreateBuilder(args);

// ----- Strongly-typed options registration -----
// Bind the "Bank" section to BankOptions; add DataAnnotation validation.
builder.Services
    .AddOptions<BankOptions>()
    .BindConfiguration(BankOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Bind the "FeatureFlags" section to FeatureFlagOptions.
builder.Services
    .AddOptions<FeatureFlagOptions>()
    .BindConfiguration(FeatureFlagOptions.SectionName);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();
app.Run();

