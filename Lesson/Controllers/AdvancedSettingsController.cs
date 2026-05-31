using Lesson.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Lesson.Controllers;

/// <summary>
/// Lesson 02-C — Advanced configuration patterns.
///
///   GET /advancedsettings/connection   — connection strings from User Secrets / env-vars
///   GET /advancedsettings/limits/{name} — named options (domestic / international)
///   GET /advancedsettings/custom/{key}  — custom IConfigurationProvider
/// </summary>
[ApiController]
[Route("advancedsettings")]
public class AdvancedSettingsController(
    IOptions<ConnectionStringOptions> connOptions,
    IOptionsMonitor<TransferLimitOptions> limitsMonitor,
    IConfiguration configuration) : ControllerBase
{
    // -------------------------------------------------------------------------
    // User Secrets / environment variables
    //
    // Connection strings are never in source control.
    // In Development  → dotnet user-secrets (secrets.json outside the repo)
    // In Production   → environment variables with __ separator:
    //                   ConnectionStrings__BankDb=Server=prod;...
    //
    // We return the server name only, never the full connection string!
    // -------------------------------------------------------------------------
    [HttpGet("connection")]
    public IActionResult GetConnection()
    {
        var opts = connOptions.Value;

        // Parse server name from connection string for display (never expose passwords)
        static string? ParseServer(string cs) =>
            cs.Split(';')
              .Select(p => p.Trim())
              .FirstOrDefault(p => p.StartsWith("Server=", StringComparison.OrdinalIgnoreCase))
              ?.Split('=')[1];

        return Ok(new
        {
            BankDbServer = ParseServer(opts.BankDb) ?? "(not set)",
            AuditDbServer = ParseServer(opts.AuditDb) ?? "(not set)",
            Note = "Full connection strings are intentionally hidden. " +
                   "Set them via User Secrets (dev) or env-vars (prod)."
        });
    }

    // -------------------------------------------------------------------------
    // Named options
    //
    // IOptionsMonitor<T>.Get("name") retrieves a specific named instance.
    // Both "domestic" and "international" are bound to different sub-sections
    // of the "TransferLimits" config key.
    // -------------------------------------------------------------------------
    [HttpGet("limits/{name}")]
    public IActionResult GetLimits(string name)
    {
        if (!string.Equals(name, "domestic", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(name, "international", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { Error = "name must be 'domestic' or 'international'" });
        }

        var limits = limitsMonitor.Get(name.ToLowerInvariant());
        return Ok(new
        {
            Name = name,
            limits.DailyLimit,
            limits.SingleTransactionLimit,
            limits.Currency
        });
    }

    // -------------------------------------------------------------------------
    // Custom configuration provider
    //
    // Values are loaded by InMemoryDbConfigurationProvider registered in Program.cs.
    // IConfiguration["CustomConfig:WelcomeMessage"] works exactly like any other
    // config key — the provider is transparent to the consumer.
    // -------------------------------------------------------------------------
    [HttpGet("custom/{key}")]
    public IActionResult GetCustom(string key)
    {
        var fullKey = $"CustomConfig:{key}";
        var value = configuration[fullKey];
        if (value is null)
            return NotFound(new { Error = $"Key 'CustomConfig:{key}' not found in custom provider." });

        return Ok(new { Key = fullKey, Value = value });
    }
}
