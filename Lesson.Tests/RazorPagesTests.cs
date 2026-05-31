using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Lesson.Data;
using FluentAssertions;
using System.Net;

namespace Lesson.Tests;

// =============================================================================
// LESSON 26-B Tests: Razor Pages
//
// Integration tests that verify the server-rendered HTML output of the
// Statement Razor Page — analogous to Spring MVC @WebMvcTest or MockMvc.
//
// Patterns shown:
//  • GET /Statement → full HTML document with statement data
//  • GET /Statement?accountNumber=ACC-007 → correct account in HTML
//  • Layout is included (nav, footer)
//  • Partial view rendered (table rows)
//  • Anti-forgery token form is present
//  • POST /Statement → redirects (PRG pattern)
// =============================================================================
public class RazorPagesTests : IClassFixture<RazorPagesTestFactory>
{
    private readonly HttpClient _client;

    public RazorPagesTests(RazorPagesTestFactory factory)
    {
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    [Fact]
    public async Task GetStatement_ReturnsHtmlWithStatementData()
    {
        var response = await _client.GetAsync("/Statement");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/html");

        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("Bank Statement");
        html.Should().Contain("ACC-001");
        html.Should().Contain("Alice Dupont");
    }

    [Fact]
    public async Task GetStatement_WithAccountNumber_ShowsCorrectAccount()
    {
        var response = await _client.GetAsync("/Statement?accountNumber=ACC-007");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("ACC-007");
    }

    [Fact]
    public async Task GetStatement_IncludesLayoutNavigation()
    {
        var response = await _client.GetAsync("/Statement");
        var html = await response.Content.ReadAsStringAsync();

        // Layout renders the <nav> and <footer>
        html.Should().Contain("<nav>");
        html.Should().Contain("Sandbox Bank");
    }

    [Fact]
    public async Task GetStatement_IncludesTransactionRows()
    {
        var response = await _client.GetAsync("/Statement");
        var html = await response.Content.ReadAsStringAsync();

        // Partial view _TransactionRow renders <tr> elements
        html.Should().Contain("<tr>");
        html.Should().Contain("Credit");
        html.Should().Contain("Debit");
    }

    [Fact]
    public async Task GetStatement_IncludesAntiForgeryForm()
    {
        var response = await _client.GetAsync("/Statement");
        var html = await response.Content.ReadAsStringAsync();

        // @Html.AntiForgeryToken() injects a hidden __RequestVerificationToken field
        html.Should().Contain("__RequestVerificationToken");
        html.Should().Contain("<form method=\"post\">");
    }

    [Fact]
    public async Task PostStatement_WithoutToken_Returns400()
    {
        // POST without anti-forgery token should be rejected with 400
        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("accountNumber", "ACC-001")
        });

        var response = await _client.PostAsync("/Statement", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}

// ---------------------------------------------------------------------------
// Test factory
// ---------------------------------------------------------------------------
public class RazorPagesTestFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection;

    public RazorPagesTestFactory()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<BankingDbContext>));
            if (descriptor is not null) services.Remove(descriptor);

            services.AddDbContext<BankingDbContext>(options =>
                options.UseSqlite(_connection));

            var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            scope.ServiceProvider
                 .GetRequiredService<BankingDbContext>()
                 .Database.Migrate();
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _connection.Dispose();
    }
}
