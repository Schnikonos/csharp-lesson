using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Lesson.Pages;

// =============================================================================
// LESSON 26-B: PageModel for the Statement Razor Page
//
// PageModel is the code-behind class that prepares data for the .cshtml view.
//
// Java parallel:
//   PageModel  → Spring MVC @Controller + Model / ModelAndView
//   OnGet()    → @GetMapping method that populates model attributes
//   OnPost()   → @PostMapping method
//
// @inject equivalent:
//   Services can be injected into both the PageModel constructor (here)
//   and directly into the .cshtml via @inject — both are shown.
// =============================================================================
public class StatementModel : PageModel
{
    private readonly ILogger<StatementModel> _logger;

    public StatementModel(ILogger<StatementModel> logger)
    {
        _logger = logger;
    }

    // Properties exposed to the view as @Model.xxx
    public string AccountNumber { get; private set; } = "ACC-001";
    public string CustomerName  { get; private set; } = "Alice Dupont";
    public string PeriodStart   { get; private set; } = "2025-01-01";
    public string PeriodEnd     { get; private set; } = "2025-01-31";
    public string Currency      { get; private set; } = "EUR";
    public decimal OpeningBalance { get; private set; } = 1_000.00m;
    public decimal ClosingBalance { get; private set; } = 1_435.25m;
    public IReadOnlyList<TransactionViewModel> Transactions { get; private set; } = [];

    // OnGet — called on GET /Statement (analogous to @GetMapping)
    public void OnGet(string accountNumber = "ACC-001")
    {
        AccountNumber = accountNumber;
        _logger.LogInformation("Rendering statement for {AccountNumber}", AccountNumber);

        Transactions = new[]
        {
            new TransactionViewModel("2025-01-05", "Credit", "EUR", 500.00m),
            new TransactionViewModel("2025-01-12", "Debit",  "EUR",  64.75m),
            new TransactionViewModel("2025-01-20", "Credit", "EUR", 200.00m),
            new TransactionViewModel("2025-01-28", "Debit",  "EUR", 200.00m),
        };
    }

    // OnPost — handles the form submission (analogous to @PostMapping)
    public IActionResult OnPost(string accountNumber)
    {
        _logger.LogInformation("Download requested for {AccountNumber}", accountNumber);
        TempData["Message"] = $"PDF download queued for {accountNumber}";
        return RedirectToPage();
    }
}

/// <summary>View model for a single transaction row (used by the partial view).</summary>
public record TransactionViewModel(string Date, string Type, string Currency, decimal Amount);
