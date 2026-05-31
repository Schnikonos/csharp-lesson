using Lesson.Templating;
using Microsoft.AspNetCore.Mvc;

namespace Lesson.Controllers;

// =============================================================================
// LESSON 10-C: Template Engine — Scriban
//
// Demonstrates generating text artefacts (emails, statements, reports) from
// named template files at runtime using the ITemplateEngine abstraction.
//
// Java parallel:
//   Thymeleaf  → ScribanTemplateEngine.RenderAsync(name, model)
//   Jinja2     → same; {{ variable }} syntax is intentionally Liquid-like
//
// Template location: Lesson/Templating/Templates/
// =============================================================================
[ApiController]
[Route("api/[controller]")]
public class TemplatingController : ControllerBase
{
    private readonly ITemplateEngine _engine;
    private readonly ILogger<TemplatingController> _logger;

    public TemplatingController(ITemplateEngine engine, ILogger<TemplatingController> logger)
    {
        _engine = engine;
        _logger = logger;
    }

    // -------------------------------------------------------------------------
    // GET /api/templating/email/{transactionId}
    // Returns a rendered plain-text transaction-confirmation email.
    // -------------------------------------------------------------------------
    [HttpGet("email/{transactionId}")]
    public async Task<IActionResult> GetTransactionEmail(string transactionId)
    {
        _logger.LogInformation("Rendering transaction email for {TransactionId}", transactionId);

        var model = new
        {
            TransactionId    = transactionId,
            CustomerName     = "Alice Dupont",
            AccountNumber    = "ACC-001",
            TransactionDate  = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd"),
            Currency         = "EUR",
            Amount           = 250.75m,
            TransactionType  = "Credit"
        };

        var text = await _engine.RenderAsync("transaction-email.txt", model);
        return Content(text, "text/plain");
    }

    // -------------------------------------------------------------------------
    // GET /api/templating/statement/{accountNumber}
    // Returns a rendered plain-text bank statement.
    // -------------------------------------------------------------------------
    [HttpGet("statement/{accountNumber}")]
    public async Task<IActionResult> GetStatement(string accountNumber)
    {
        var model = new
        {
            AccountNumber   = accountNumber,
            CustomerName    = "Alice Dupont",
            PeriodStart     = "2025-01-01",
            PeriodEnd       = "2025-01-31",
            Currency        = "EUR",
            OpeningBalance  = 1000.00m,
            ClosingBalance  = 1435.25m,
            Transactions    = new[]
            {
                new { Date = "2025-01-05", Type = "Credit",  Currency = "EUR", Amount = 500.00m },
                new { Date = "2025-01-12", Type = "Debit",   Currency = "EUR", Amount = 64.75m  },
                new { Date = "2025-01-20", Type = "Credit",  Currency = "EUR", Amount = 200.00m },
                new { Date = "2025-01-28", Type = "Debit",   Currency = "EUR", Amount = 200.00m }
            }
        };

        var text = await _engine.RenderAsync("bank-statement.txt", model);
        return Content(text, "text/plain");
    }

    // -------------------------------------------------------------------------
    // GET /api/templating/report/{year}/{month}
    // Returns a rendered plain-text monthly activity report.
    // -------------------------------------------------------------------------
    [HttpGet("report/{year:int}/{month:int}")]
    public async Task<IActionResult> GetMonthlyReport(int year, int month)
    {
        var model = new
        {
            Year               = year,
            Month              = new DateTime(year, month, 1).ToString("MMMM"),
            GeneratedAt        = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") + " UTC",
            Currency           = "EUR",
            TotalDeposits      = 28_450.00m,
            TotalWithdrawals   = 11_230.50m,
            NetCashFlow        = 17_219.50m,
            ActiveAccounts     = 142,
            TopTransactions    = new[]
            {
                new { Date = $"{year}-{month:D2}-03", Account = "ACC-007", Type = "Credit",  Currency = "EUR", Amount = 8_000.00m },
                new { Date = $"{year}-{month:D2}-08", Account = "ACC-012", Type = "Debit",   Currency = "EUR", Amount = 4_500.00m },
                new { Date = $"{year}-{month:D2}-14", Account = "ACC-001", Type = "Credit",  Currency = "EUR", Amount = 3_200.00m },
                new { Date = $"{year}-{month:D2}-19", Account = "ACC-033", Type = "Debit",   Currency = "EUR", Amount = 2_900.00m },
                new { Date = $"{year}-{month:D2}-25", Account = "ACC-021", Type = "Credit",  Currency = "EUR", Amount = 1_750.00m }
            }
        };

        var text = await _engine.RenderAsync("monthly-report.txt", model);
        return Content(text, "text/plain");
    }

    // -------------------------------------------------------------------------
    // POST /api/templating/inline
    // Renders an arbitrary inline template string sent in the request body.
    // Useful for testing Scriban syntax interactively.
    // -------------------------------------------------------------------------
    [HttpPost("inline")]
    public async Task<IActionResult> RenderInline([FromBody] InlineTemplateRequest request)
    {
        var text = await _engine.RenderStringAsync(request.Template, request.Model ?? new { });
        return Content(text, "text/plain");
    }
}

public record InlineTemplateRequest(string Template, object? Model);
