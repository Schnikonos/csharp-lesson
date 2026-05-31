# Lesson 02-C — Advanced Configuration: User Secrets, Named Options, Custom Provider

> **Branch:** `lesson/02-configuration/c-advanced`
> **Prerequisites:** Lesson 02-B (IOptions, ValidateOnStart)

---

## What you will learn

| Topic | C# / ASP.NET Core | Java / Spring Boot parallel |
|---|---|---|
| Dev-time secrets | `dotnet user-secrets` | Spring Vault / application-local.properties |
| Production secrets | Env-vars with `__` separator | `SPRING_DATASOURCE_URL` env-var |
| Named options | `IOptionsMonitor<T>.Get("name")` | `@Qualifier` / named `@Bean` |
| Custom config source | `IConfigurationSource` + `ConfigurationProvider` | Custom `PropertySource<T>` |

---

## 1. User Secrets

Secrets that must never be committed (passwords, API keys, connection strings).

```bash
# Enable (adds <UserSecretsId> to .csproj)
dotnet user-secrets init

# Store a secret
dotnet user-secrets set "ConnectionStrings:BankDb" "Server=localhost;..."

# List / remove
dotnet user-secrets list
dotnet user-secrets remove "ConnectionStrings:BankDb"
```

Stored **outside the repo** at:
- Windows: `%APPDATA%\Microsoft\UserSecrets\{id}\secrets.json`
- Linux/Mac: `~/.microsoft/usersecrets/{id}/secrets.json`

Loaded automatically only when `IsDevelopment()` is true.

---

## 2. Environment Variables

Override any key in any environment. **Double underscore `__` replaces `:` as the hierarchy separator.**

```powershell
$env:ConnectionStrings__BankDb = "Server=prod;..."
$env:Bank__MaxTransferLimit    = "100000"
```

Config loading order (later wins):

```
appsettings.json
  appsettings.{Env}.json
    User Secrets  (Development only)
      Environment Variables
        Command-line arguments
```

---

## 3. Named Options

Register the **same POCO** under multiple logical names, each bound to a different config sub-section:

```csharp
// Registration in Program.cs
services.AddOptions<TransferLimitOptions>("domestic")
    .BindConfiguration("TransferLimits:Domestic");
services.AddOptions<TransferLimitOptions>("international")
    .BindConfiguration("TransferLimits:International");

// Retrieval — IOptionsMonitor<T> required (IOptions/IOptionsSnapshot don't support named instances)
public class FxService(IOptionsMonitor<TransferLimitOptions> limits)
{
    var dom  = limits.Get("domestic");
    var intl = limits.Get("international");
}
```

Java parallel: `@Qualifier("domestic")` on a `@Bean`.

---

## 4. Custom IConfigurationProvider

Two-class pattern — a **source** (factory) and a **provider** (loader):

```csharp
// 1. Source — registered on the builder
public class MyDbConfigSource : IConfigurationSource
{
    public IConfigurationProvider Build(IConfigurationBuilder builder)
        => new MyDbConfigProvider();
}

// 2. Provider — inherits ConfigurationProvider; stores values in Data dict
public class MyDbConfigProvider : ConfigurationProvider
{
    public override void Load()
    {
        // Query DB, decrypt file, call HTTP endpoint...
        Data["MySection:Key"] = "value from db";
    }
}

// Registration
((IConfigurationBuilder)builder.Configuration).Add(new MyDbConfigSource());
```

Once registered, `IConfiguration["MySection:Key"]` works exactly like any `appsettings.json` key — the source is transparent to the consumer.

---

## Endpoints

| Method | URL | Description |
|---|---|---|
| `GET` | `/advancedsettings/connection` | Connection strings (server name only — passwords hidden) |
| `GET` | `/advancedsettings/limits/domestic` | Named options — domestic transfer limits |
| `GET` | `/advancedsettings/limits/international` | Named options — international transfer limits |
| `GET` | `/advancedsettings/custom/{key}` | Value from `InMemoryDbConfigurationProvider` |

---

## Tests

```bash
dotnet test
# 19 tests total: 4 new (02-C Feature Flags) + 15 carried from 02-C Named Options + 02-A/B
```

| Test | What it verifies |
|---|---|
| `GetConnection_ReturnsServerNameOnly` | Server name parsed; passwords never exposed |
| `GetLimits_Domestic_ReturnsDomesticLimits` | Named options "domestic" binding |
| `GetLimits_International_ReturnsInternationalLimits` | Named options "international" binding |
| `GetLimits_UnknownName_ReturnsBadRequest` | Invalid `name` param returns 400 |
| `GetCustom_KnownKey_ReturnsValueFromCustomProvider` | Custom provider key read via `IConfiguration` |
| `GetCustom_UnknownKey_ReturnsNotFound` | Missing key returns 404 |
| `FeatureGate_EnabledFlag_Returns200` | `[FeatureGate]` passes when flag is `true` |
| `FeatureGate_DisabledFlag_Returns404` | `[FeatureGate]` blocks with 404 when flag is `false` |
| `ProgrammaticCheck_DisabledFlag_ReturnsDisabledBody` | `IFeatureManager.IsEnabledAsync` drives graceful degradation |
| `AllEndpoint_ReturnsAllFlags` | `/feature-demo/all` lists every flag with its current state |

---

## 02-C Part 2 — Feature Flags (Microsoft.FeatureManagement)

### Why Feature Flags?

Feature flags decouple **deployment** from **release**. New code ships to production but remains
inert until a flag is flipped in configuration — no redeploy needed.

| Concept | .NET | Java |
|---|---|---|
| Library | `Microsoft.FeatureManagement.AspNetCore` | Togglz / FF4J / LaunchDarkly |
| Attribute gate | `[FeatureGate("FlagName")]` | `@TogglzFeatureActive` |
| Programmatic | `IFeatureManager.IsEnabledAsync(name)` | `FeatureManager.isActive(Feature.X)` |
| Percentage rollout | `PercentageFilter` (built-in) | `GradualFeatureActivationStrategy` |
| Configuration root | `"FeatureManagement"` section in `appsettings.json` | YAML `features:` block |

### Registration (`Program.cs`)

```csharp
builder.Services
    .AddFeatureManagement(builder.Configuration.GetSection("FeatureManagement"))
    .AddFeatureFilter<PercentageFilter>();
```

### Configuration (`appsettings.json`)

```json
"FeatureManagement": {
  "InstantTransfer": true,           // simple boolean — always on
  "EnhancedStatements": false,       // simple boolean — always off
  "DarkMode": {                      // percentage rollout — on for 25 % of requests
    "EnabledFor": [
      { "Name": "Microsoft.Percentage", "Parameters": { "Value": 25 } }
    ]
  }
}
```

### Endpoints

| Route | Mechanism | Behaviour when disabled |
|---|---|---|
| `GET /feature-demo/instant-transfer` | `[FeatureGate]` attribute | 404 Not Found (hard block) |
| `GET /feature-demo/enhanced-statements` | `IFeatureManager` inline check | 200 with `"status":"disabled"` (graceful) |
| `GET /feature-demo/all` | `IFeatureManager` loop | 200 — full flag dashboard |

### Key files

| File | Role |
|---|---|
| `Controllers/FeatureFlagController.cs` | Endpoints + `FeatureFlags` constants |
| `appsettings.json` ? `"FeatureManagement"` | Flag values (boolean or filter-based) |
| `Program.cs` | `AddFeatureManagement` + `PercentageFilter` wiring |
| `Lesson.Tests/FeatureFlagTests.cs` | Integration tests with in-memory config overrides |

---

## Exercises

1. Run `dotnet user-secrets set "Bank:Name" "Secret Override Bank"` and confirm `GET /bankinfo` shows the override in Development.
2. Extend `InMemoryDbConfigurationProvider` to support hot-reload by calling `OnReload()` on a timer.
3. Create named `FeatureFlagOptions` instances per tenant and route to the correct one via a request header.
4. Write a `SecureConfigurationProvider` that reads AES-encrypted values from a file and decrypts them in `Load()`.
5. Add a `TimeWindowFilter` that enables `InstantTransfer` only during business hours (09:00–17:00 UTC).
6. Implement a `UserIdFilter` that activates `EnhancedStatements` for a hard-coded list of beta users.
