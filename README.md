# Lesson 02-A — Configuration Basics

> **Branch:** `lesson/02-configuration/a-basic`
> **Prerequisites:** Lesson 01 series (controllers, DI)

---

## What you will learn

| Topic | C# / ASP.NET Core | Java / Spring Boot parallel |
|---|---|---|
| Primary config file | `appsettings.json` | `application.properties` / `application.yml` |
| Environment overrides | `appsettings.Development.json` | `application-dev.properties` |
| Active environment | `ASPNETCORE_ENVIRONMENT` | `spring.profiles.active` |
| Read a single value | `IConfiguration["Bank:Name"]` | `@Value("${bank.name}")` |
| Typed read with default | `config.GetValue<decimal>("...", 0)` | `env.getProperty("...", 0)` |
| Read a whole section | `config.GetSection("FeatureFlags").GetChildren()` | `@ConfigurationProperties` |
| Environment checks | `env.IsDevelopment()` / `IsProduction()` | `@Profile("dev")` |

---

## Configuration loading order

ASP.NET Core merges sources in this order — **later sources win**:

```
1. appsettings.json                  (base values)
2. appsettings.{Environment}.json    (environment override)
3. User Secrets                      (Development only — Lesson 02-C)
4. Environment variables
5. Command-line arguments
```

The active environment is read from `ASPNETCORE_ENVIRONMENT`. In
`launchSettings.json` it is pre-set to `"Development"` for local runs.

---

## Key concepts

### Nested keys with `:`

JSON nesting is flattened with `:` as separator:

```json
{
  "Bank": {
    "Contact": { "Email": "..." }
  }
}
```

```csharp
var email = config["Bank:Contact:Email"];
```

### Three access patterns (see `BankInfoController.cs`)

```csharp
// 1. String indexer — returns string? (null if missing)
var name = config["Bank:Name"];

// 2. GetValue<T> — typed, safe default
var limit = config.GetValue<decimal>("Bank:MaxTransferLimit", defaultValue: 0);

// 3. GetSection — iterate a whole sub-tree
var flags = config.GetSection("FeatureFlags")
    .GetChildren()
    .ToDictionary(e => e.Key, e => bool.Parse(e.Value!));
```

> **Lesson 02-B** introduces `IOptions<T>`, `IOptionsSnapshot<T>`, and
> `IOptionsMonitor<T>` — the recommended pattern for injecting config as
> strongly-typed POCOs into services.

### Environment-specific values

`appsettings.Development.json` overrides `Bank:MaxTransferLimit` to `100`
(vs `50000` in base). Run locally and call `GET /bankinfo/environment` to
see the override in effect.

---

## Endpoints

| Method | URL | Description |
|---|---|---|
| `GET` | `/bankinfo` | Bank name, SWIFT, contact, max-transfer limit |
| `GET` | `/bankinfo/features` | Feature flags as `{ key: bool }` dictionary |
| `GET` | `/bankinfo/environment` | Active environment name + effective MaxTransferLimit |

---

## Tests

```bash
dotnet test
# 4 integration tests — all use AddInMemoryCollection for deterministic config
```

| Test | What it verifies |
|---|---|
| `Get_ReturnsBankInfo_WithExpectedValues` | Correct field mapping from config to JSON response |
| `GetFeatureFlags_ReturnsAllFlags` | Boolean flags correctly parsed |
| `GetEnvironment_ReturnsEnvironmentName` | Environment name and MaxTransferLimit |
| `Get_WithOverriddenConfig_ReturnsOverriddenValue` | In-memory override wins over appsettings.json |

---

## Exercises

1. Add `GET /bankinfo/raw/{key}` that accepts any config key path and returns its raw string value — `404` if missing.
2. Add a `"SupportedCurrencies"` array to `appsettings.json` and read it with `config.GetSection("SupportedCurrencies").Get<string[]>()`.
3. Override `Bank:MaxTransferLimit` via an environment variable (`Bank__MaxTransferLimit=1` — double underscore is the env-var hierarchy separator) and verify it takes precedence.
4. Preview Lesson 02-B: try wrapping a `BankOptions` POCO and registering it with `services.Configure<BankOptions>(config.GetSection("Bank"))`.

