using Microsoft.AspNetCore.Mvc;

namespace Lesson.Controllers;

// =============================================================================
// LESSON 02-A: Reading configuration via IConfiguration
//
// IConfiguration is ASP.NET Core's unified view over all configuration sources
// (appsettings.json, environment variables, command-line, etc.).
//
// It is registered automatically — inject it into any controller or service.
//
// Three access patterns demonstrated below:
//
//  1. Direct string indexer     — IConfiguration["Bank:Name"]
//  2. GetValue<T>               — typed read with a default fallback
//  3. GetSection / Bind         — read a whole sub-tree into a dictionary or POCO
//
// Java parallel:
//   @Value("${bank.name}")                     →  IConfiguration["Bank:Name"]
//   environment.getProperty("bank.name", "?")  →  config.GetValue<string>("Bank:Name", "?")
//   @ConfigurationProperties(prefix="bank")    →  config.GetSection("Bank")  (see Lesson 02-B)
// =============================================================================

[ApiController]
[Route("[controller]")]
public class BankInfoController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _env;

    // Both IConfiguration and IWebHostEnvironment are registered by the
    // framework automatically — no explicit DI registration needed.
    public BankInfoController(IConfiguration config, IWebHostEnvironment env)
    {
        _config = config;
        _env    = env;
    }

    // GET /bankinfo
    // -------------------------------------------------------------------------
    // Returns a summary of the Bank config section.
    //
    // ACCESS PATTERN 1 — Direct string indexer
    //   config["Bank:Name"]
    //   Nested keys are separated by ":" — equivalent to YAML dot notation.
    //   Returns null if the key does not exist.
    //
    // ACCESS PATTERN 2 — GetValue<T>(key, defaultValue)
    //   Performs type conversion; returns the default if key is missing or
    //   conversion fails. Safer than the indexer for non-string types.
    //
    // ACCESS PATTERN 3 — GetSection(key).GetChildren()
    //   Returns all immediate child keys of a section — useful for iterating
    //   over dynamic sub-trees.
    // -------------------------------------------------------------------------
    [HttpGet]
    [ProducesResponseType<BankInfoResponse>(StatusCodes.Status200OK)]
    public IActionResult Get()
    {
        // Pattern 1: direct indexer — returns string? (null if missing)
        var name      = _config["Bank:Name"];
        var swiftCode = _config["Bank:SwiftCode"];
        var country   = _config["Bank:Country"];
        var email     = _config["Bank:Contact:Email"];  // nested key with ":"

        // Pattern 2: GetValue<T> — typed conversion with a safe default
        var maxTransfer = _config.GetValue<decimal>("Bank:MaxTransferLimit", defaultValue: 0);

        return Ok(new BankInfoResponse(
            Name:            name ?? "unknown",
            SwiftCode:       swiftCode ?? "unknown",
            Country:         country ?? "unknown",
            ContactEmail:    email ?? "unknown",
            MaxTransferLimit: maxTransfer,
            Environment:     _env.EnvironmentName));
    }

    // GET /bankinfo/features
    // -------------------------------------------------------------------------
    // Returns the FeatureFlags section as a dictionary.
    //
    // ACCESS PATTERN 3 — GetSection + GetChildren
    //   GetSection("FeatureFlags") returns an IConfigurationSection sub-tree.
    //   GetChildren() enumerates its immediate key-value pairs.
    //
    // This pattern is useful when the set of keys is dynamic or unknown at
    // compile time. For known shapes use IOptions<T> (covered in Lesson 02-B).
    // -------------------------------------------------------------------------
    [HttpGet("features")]
    [ProducesResponseType<Dictionary<string, bool>>(StatusCodes.Status200OK)]
    public IActionResult GetFeatureFlags()
    {
        // GetSection returns a sub-tree; GetChildren enumerates its entries.
        var flags = _config
            .GetSection("FeatureFlags")
            .GetChildren()
            .ToDictionary(
                entry => entry.Key,
                entry => bool.TryParse(entry.Value, out var v) && v);

        return Ok(flags);
    }

    // GET /bankinfo/environment
    // -------------------------------------------------------------------------
    // Shows which environment is active and how ASP.NET Core determines it.
    //
    // IWebHostEnvironment exposes:
    //   .EnvironmentName   — raw string ("Development", "Production", …)
    //   .IsDevelopment()   — true when EnvironmentName == "Development"
    //   .IsProduction()    — true when EnvironmentName == "Production"
    //   .IsStaging()       — true when EnvironmentName == "Staging"
    //   .IsEnvironment(x)  — custom environment check
    //
    // Java parallel:
    //   @Profile("dev") / spring.profiles.active  →  ASPNETCORE_ENVIRONMENT
    //   Environment.acceptsProfiles("dev")         →  env.IsDevelopment()
    // -------------------------------------------------------------------------
    [HttpGet("environment")]
    [ProducesResponseType<EnvironmentResponse>(StatusCodes.Status200OK)]
    public IActionResult GetEnvironment()
    {
        return Ok(new EnvironmentResponse(
            Name:          _env.EnvironmentName,
            IsDevelopment: _env.IsDevelopment(),
            IsProduction:  _env.IsProduction(),
            // In Development the MaxTransferLimit is overridden to 100 (see
            // appsettings.Development.json). This shows the override in action.
            MaxTransferLimit: _config.GetValue<decimal>("Bank:MaxTransferLimit")));
    }
}

// ─── Response DTOs ────────────────────────────────────────────────────────────
// Records are the idiomatic C# DTO: immutable, value-equality, terse syntax.
// Java parallel: Java 16+ record / immutable POJO with Lombok @Value

public record BankInfoResponse(
    string Name,
    string SwiftCode,
    string Country,
    string ContactEmail,
    decimal MaxTransferLimit,
    string Environment);

public record EnvironmentResponse(
    string Name,
    bool IsDevelopment,
    bool IsProduction,
    decimal MaxTransferLimit);
