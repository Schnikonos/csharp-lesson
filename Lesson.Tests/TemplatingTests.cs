using Lesson.Templating;
using FluentAssertions;

namespace Lesson.Tests;

// =============================================================================
// LESSON 10-C Tests: Scriban Template Engine
//
// Validates the ITemplateEngine / ScribanTemplateEngine implementation.
//
// Key patterns:
//  • RenderStringAsync — inline template, no disk I/O
//  • RenderAsync       — named file from the Templates directory
//  • Error handling    — malformed template raises InvalidOperationException
// =============================================================================
public class TemplatingTests
{
    private readonly ScribanTemplateEngine _engine;

    public TemplatingTests()
    {
        // Point at the templates directory copied alongside the test binary
        var templateRoot = Path.Combine(AppContext.BaseDirectory, "Templating", "Templates");
        _engine = new ScribanTemplateEngine(templateRoot);
    }

    // -------------------------------------------------------------------------
    // RenderStringAsync — basic interpolation
    // -------------------------------------------------------------------------
    [Fact]
    public async Task RenderStringAsync_SimpleInterpolation_ReturnsRenderedText()
    {
        var template = "Hello {{ name }}, your balance is {{ currency }} {{ balance }}.";
        var model = new { Name = "Alice", Currency = "EUR", Balance = 1234.56m };

        var result = await _engine.RenderStringAsync(template, model);

        result.Should().Be("Hello Alice, your balance is EUR 1234.56.");
    }

    [Fact]
    public async Task RenderStringAsync_ForLoop_RendersAllItems()
    {
        var template = "{{- for item in items }}{{ item }}\n{{- end }}";
        var model = new { Items = new[] { "alpha", "beta", "gamma" } };

        var result = await _engine.RenderStringAsync(template, model);

        result.Should().Contain("alpha").And.Contain("beta").And.Contain("gamma");
    }

    [Fact]
    public async Task RenderStringAsync_Conditional_RendersCorrectBranch()
    {
        var template = "{{ if is_vip }}VIP customer{{ else }}Standard customer{{ end }}";

        var vipResult = await _engine.RenderStringAsync(template, new { IsVip = true });
        var stdResult = await _engine.RenderStringAsync(template, new { IsVip = false });

        vipResult.Should().Contain("VIP customer");
        stdResult.Should().Contain("Standard customer");
    }

    [Fact]
    public async Task RenderStringAsync_MathFilter_FormatsDecimal()
    {
        var template = "{{ amount | math.format \"0.00\" }}";
        var model = new { Amount = 999.9m };

        var result = await _engine.RenderStringAsync(template, model);

        result.Should().Be("999.90");
    }

    [Fact]
    public async Task RenderStringAsync_InvalidTemplate_ThrowsInvalidOperationException()
    {
        var badTemplate = "{{ for x in }}broken";

        Func<Task> act = () => _engine.RenderStringAsync(badTemplate, new { });

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*parse errors*");
    }

    // -------------------------------------------------------------------------
    // RenderAsync — file-based templates
    // -------------------------------------------------------------------------
    [Fact]
    public async Task RenderAsync_TransactionEmailTemplate_ContainsExpectedFields()
    {
        var model = new
        {
            TransactionId   = "TXN-9999",
            CustomerName    = "Bob Martin",
            AccountNumber   = "ACC-042",
            TransactionDate = "2025-06-01",
            Currency        = "USD",
            Amount          = 300.00m,
            TransactionType = "Debit"
        };

        var result = await _engine.RenderAsync("transaction-email.txt", model);

        result.Should().Contain("TXN-9999");
        result.Should().Contain("Bob Martin");
        result.Should().Contain("ACC-042");
        result.Should().Contain("USD");
        result.Should().Contain("300.00");
        result.Should().Contain("Debit");
    }

    [Fact]
    public async Task RenderAsync_BankStatementTemplate_ContainsAllTransactions()
    {
        var model = new
        {
            AccountNumber  = "ACC-001",
            CustomerName   = "Alice Dupont",
            PeriodStart    = "2025-01-01",
            PeriodEnd      = "2025-01-31",
            Currency       = "EUR",
            OpeningBalance = 1000.00m,
            ClosingBalance = 1435.25m,
            Transactions   = new[]
            {
                new { Date = "2025-01-05", Type = "Credit", Currency = "EUR", Amount = 500.00m },
                new { Date = "2025-01-12", Type = "Debit",  Currency = "EUR", Amount = 64.75m  }
            }
        };

        var result = await _engine.RenderAsync("bank-statement.txt", model);

        result.Should().Contain("ACC-001");
        result.Should().Contain("1000.00");
        result.Should().Contain("1435.25");
        result.Should().Contain("2025-01-05");
        result.Should().Contain("Credit");
        result.Should().Contain("64.75");
    }

    [Fact]
    public async Task RenderAsync_MonthlyReportTemplate_ContainsTopTransactions()
    {
        var model = new
        {
            Year             = 2025,
            Month            = "January",
            GeneratedAt      = "2025-02-01 00:00:00 UTC",
            Currency         = "EUR",
            TotalDeposits    = 28_450.00m,
            TotalWithdrawals = 11_230.50m,
            NetCashFlow      = 17_219.50m,
            ActiveAccounts   = 142,
            TopTransactions  = new[]
            {
                new { Date = "2025-01-03", Account = "ACC-007", Type = "Credit", Currency = "EUR", Amount = 8_000.00m }
            }
        };

        var result = await _engine.RenderAsync("monthly-report.txt", model);

        result.Should().Contain("2025");
        result.Should().Contain("January");
        result.Should().Contain("28450.00");
        result.Should().Contain("ACC-007");
        result.Should().Contain("8000.00");
    }

    [Fact]
    public async Task RenderAsync_MissingTemplate_ThrowsFileNotFoundException()
    {
        Func<Task> act = () => _engine.RenderAsync("does-not-exist.txt", new { });

        await act.Should().ThrowAsync<FileNotFoundException>();
    }
}
