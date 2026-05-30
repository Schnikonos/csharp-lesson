using Lesson.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Lesson.Controllers;

/// <summary>
/// Lesson 02-B — Strongly-typed options.
///
/// Demonstrates the three IOptions interfaces:
///   GET /banksettings           — IOptions&lt;T&gt;        (singleton, frozen at startup)
///   GET /banksettings/snapshot  — IOptionsSnapshot&lt;T&gt; (scoped, reflects file changes per-request)
///   GET /banksettings/monitor   — IOptionsMonitor&lt;T&gt;  (singleton, always latest)
///   GET /banksettings/flags     — feature flags via IOptions&lt;FeatureFlagOptions&gt;
///
/// Java parallel: @ConfigurationProperties injected through constructor.
/// </summary>
[ApiController]
[Route("banksettings")]
public class BankSettingsController(
    IOptions<BankOptions> options,
    IOptionsSnapshot<BankOptions> snapshot,
    IOptionsMonitor<BankOptions> monitor,
    IOptions<FeatureFlagOptions> featureOptions) : ControllerBase
{
    // -------------------------------------------------------------------------
    // IOptions<T> — singleton lifetime
    // Value is bound once at startup and never changes during the app lifetime.
    // Use this for settings that are not expected to change while running.
    // -------------------------------------------------------------------------
    [HttpGet]
    public IActionResult Get()
    {
        var bank = options.Value;
        return Ok(new
        {
            Source = nameof(IOptions<BankOptions>),
            bank.Name,
            bank.SwiftCode,
            bank.Country,
            bank.MaxTransferLimit,
            Contact = new { bank.Contact.Email, bank.Contact.Phone }
        });
    }

    // -------------------------------------------------------------------------
    // IOptionsSnapshot<T> — scoped lifetime (one instance per HTTP request)
    // Re-reads configuration at the start of each request, so changes to
    // appsettings.json on disk are picked up without restarting the app
    // (requires reloadOnChange: true, which is the default in CreateBuilder).
    // Cannot be injected into singletons — use IOptionsMonitor instead.
    // -------------------------------------------------------------------------
    [HttpGet("snapshot")]
    public IActionResult GetSnapshot()
    {
        var bank = snapshot.Value;
        return Ok(new
        {
            Source = nameof(IOptionsSnapshot<BankOptions>),
            bank.Name,
            bank.MaxTransferLimit
        });
    }

    // -------------------------------------------------------------------------
    // IOptionsMonitor<T> — singleton lifetime, live value
    // .CurrentValue always reflects the most recent configuration.
    // Suitable for singletons and background services.
    // Also supports change callbacks: monitor.OnChange(opts => { ... })
    // -------------------------------------------------------------------------
    [HttpGet("monitor")]
    public IActionResult GetMonitor()
    {
        var bank = monitor.CurrentValue;
        return Ok(new
        {
            Source = nameof(IOptionsMonitor<BankOptions>),
            bank.Name,
            bank.MaxTransferLimit
        });
    }

    // -------------------------------------------------------------------------
    // Feature flags — FeatureFlagOptions section
    // -------------------------------------------------------------------------
    [HttpGet("flags")]
    public IActionResult GetFlags()
    {
        var flags = featureOptions.Value;
        return Ok(flags);
    }
}
