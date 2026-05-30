// =============================================================================
// LESSON 02-C: Advanced Configuration
//
// Topics covered:
//
// 1. USER SECRETS
//    dotnet user-secrets set "ConnectionStrings:BankDb" "Server=..."
//    Stored outside the repo at:
//      %APPDATA%\Microsoft\UserSecrets\{UserSecretsId}\secrets.json  (Windows)
//      ~/.microsoft/usersecrets/{UserSecretsId}/secrets.json         (Linux/Mac)
//    Loaded automatically in Development. Never committed.
//    Java parallel: Spring's @Value + system properties / Vault.
//
// 2. ENVIRONMENT VARIABLES
//    Set hierarchical keys using __ (double underscore) as separator:
//      ConnectionStrings__BankDb=Server=prod;...
//    Env-vars override appsettings and User Secrets (loaded last).
//    Java parallel: SPRING_DATASOURCE_URL env-var.
//
// 3. NAMED OPTIONS
//    Register the same POCO multiple times under different names:
//      services.AddOptions<T>("domestic").BindConfiguration("...");
//      services.AddOptions<T>("international").BindConfiguration("...");
//    Retrieve with IOptionsMonitor<T>.Get("domestic").
//    Java parallel: @Qualifier / named @Bean.
//
// 4. CUSTOM IConfigurationProvider
//    Implement IConfigurationSource + IConfigurationProvider to pull config
//    from any source (database, HTTP endpoint, encrypted file, etc.).
//    Java parallel: custom PropertySource in Spring.
// =============================================================================

using Lesson.Configuration;
using Lesson.Options;

var builder = WebApplication.CreateBuilder(args);

// ----- Options from 02-B (unchanged) -----
builder.Services
    .AddOptions<BankOptions>()
    .BindConfiguration(BankOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddOptions<FeatureFlagOptions>()
    .BindConfiguration(FeatureFlagOptions.SectionName);

// ----- 02-C: Connection strings (User Secrets / env-var) -----
// In Development, BankDb is loaded from User Secrets (set with dotnet user-secrets).
// In Production, set the env-var:  ConnectionStrings__BankDb=Server=prod;...
builder.Services
    .AddOptions<ConnectionStringOptions>()
    .BindConfiguration(ConnectionStringOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

// ----- 02-C: Named options -----
// Same POCO, two named instances bound to different sub-sections.
builder.Services
    .AddOptions<TransferLimitOptions>("domestic")
    .BindConfiguration("TransferLimits:Domestic")
    .ValidateDataAnnotations();

builder.Services
    .AddOptions<TransferLimitOptions>("international")
    .BindConfiguration("TransferLimits:International")
    .ValidateDataAnnotations();

// ----- 02-C: Custom configuration provider -----
// Adds a simulated "database" config source (read-only key/value store).
// Cast to IConfigurationBuilder to access the Add extension method unambiguously.
((IConfigurationBuilder)builder.Configuration).Add(new InMemoryDbConfigurationSource
{
    Data = new Dictionary<string, string?>
    {
        ["CustomConfig:WelcomeMessage"] = "Hello from the custom provider!",
        ["CustomConfig:MaxRetries"] = "3",
        ["CustomConfig:ServiceUrl"] = "https://api.acmebank.internal"
    }
});

builder.Services.AddControllers();
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();
app.Run();

