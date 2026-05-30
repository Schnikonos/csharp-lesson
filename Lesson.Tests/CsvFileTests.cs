using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Lesson.Data;

namespace Lesson.Tests;

/// <summary>
/// Lesson 10-B integration tests — IFormFile, CsvHelper, System.Text.Json async IO.
/// </summary>
public class CsvFileTests : IClassFixture<CsvTestFactory>
{
    private readonly HttpClient _client;

    public CsvFileTests(CsvTestFactory factory) =>
        _client = factory.CreateClient();

    // ── CSV import via IFormFile ──────────────────────────────────────────────

    private static MultipartFormDataContent BuildCsvUpload(string csv, string fileName = "transactions.csv")
    {
        var content = new MultipartFormDataContent();
        var fileBytes = Encoding.UTF8.GetBytes(csv);
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/csv");
        content.Add(fileContent, "file", fileName);
        return content;
    }

    [Fact]
    public async Task Import_ValidCsv_Returns200WithRecords()
    {
        var csv = "date,account_id,amount,description\n2025-01-15,ACC001,1500.00,Salary\n2025-01-16,ACC001,-40.00,Groceries\n";
        using var form = BuildCsvUpload(csv);

        var response = await _client.PostAsync("/files/csv/import", form);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ImportResult>();
        Assert.Equal(2, body!.recordCount);
        Assert.Equal("transactions.csv", body.fileName);
    }

    [Fact]
    public async Task Import_EmptyFile_Returns400()
    {
        using var form = BuildCsvUpload("", "empty.csv");
        var response = await _client.PostAsync("/files/csv/import", form);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Import_CsvRecordsHaveCorrectValues()
    {
        var csv = "date,account_id,amount,description\n2025-03-01,ACC099,999.99,Bonus\n";
        using var form = BuildCsvUpload(csv);

        var response = await _client.PostAsync("/files/csv/import", form);
        var body = await response.Content.ReadFromJsonAsync<ImportResult>();
        Assert.Single(body!.records);
        Assert.Equal("ACC099", body.records[0].accountId);
        Assert.Equal(999.99m, body.records[0].amount);
    }

    // ── CSV export ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Export_Returns200WithCsvContentType()
    {
        var response = await _client.GetAsync("/files/csv/export");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Export_CsvContainsHeaderAndRows()
    {
        var response = await _client.GetAsync("/files/csv/export");
        var text = await response.Content.ReadAsStringAsync();
        Assert.Contains("date", text);
        Assert.Contains("ACC001", text);
    }

    // ── JSON async IO ─────────────────────────────────────────────────────────

    [Fact]
    public async Task SaveJson_Returns200WithPath()
    {
        var payload = new { bank = "Acme", year = 2025 };
        var response = await _client.PostAsJsonAsync("/files/json/save", payload);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<SaveResult>();
        Assert.NotNull(result?.path);

        if (File.Exists(result!.path)) File.Delete(result.path);
    }

    [Fact]
    public async Task SaveThenLoadJson_RoundTrips()
    {
        var payload = new { bank = "Acme", amount = 42.5m };
        var saveResponse = await _client.PostAsJsonAsync("/files/json/save", payload);
        var saved = await saveResponse.Content.ReadFromJsonAsync<SaveResult>();
        Assert.NotNull(saved?.path);

        var loadResponse = await _client.GetAsync($"/files/json/load?path={Uri.EscapeDataString(saved!.path)}");
        Assert.Equal(HttpStatusCode.OK, loadResponse.StatusCode);

        var json = await loadResponse.Content.ReadAsStringAsync();
        Assert.Contains("Acme", json);

        if (File.Exists(saved.path)) File.Delete(saved.path);
    }

    [Fact]
    public async Task LoadJson_MissingFile_Returns404()
    {
        var response = await _client.GetAsync("/files/json/load?path=/no/such/file.json");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── Response shapes ───────────────────────────────────────────────────────
    private record ImportResult(string fileName, long sizeBytes, int recordCount, RecordDto[] records);
    private record RecordDto(string date, string accountId, decimal amount, string description);
    private record SaveResult(string path);
}

public class CsvTestFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection;

    public CsvTestFactory()
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
