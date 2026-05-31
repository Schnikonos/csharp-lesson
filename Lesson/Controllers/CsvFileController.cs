using CsvHelper;
using CsvHelper.Configuration;
using Lesson.FileHandling;
using Microsoft.AspNetCore.Mvc;
using System.Globalization;
using System.Text.Json;

namespace Lesson.Controllers;

/// <summary>
/// Lesson 10-B — IFormFile upload, CsvHelper, System.Text.Json async file IO.
///
/// New concepts vs 10-A:
///   IFormFile      — ASP.NET Core's abstraction for multipart/form-data file uploads
///                    Java parallel: @RequestParam MultipartFile file
///   CsvHelper      — third-party library for robust CSV parsing/writing
///                    Java parallel: OpenCSV / Apache Commons CSV
///   JsonSerializer — System.Text.Json async serialisation to/from streams
///                    Java parallel: Jackson ObjectMapper.readValue / writeValue
/// </summary>
[ApiController]
[Route("files/csv")]
public class CsvFileController(ILogger<CsvFileController> logger) : ControllerBase
{
    // POST /files/csv/import — upload a CSV and parse it
    // Consumes multipart/form-data; field name = "file"
    // Java parallel: @PostMapping + @RequestParam("file") MultipartFile
    [HttpPost("import")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> ImportCsv(IFormFile file, CancellationToken ct)
    {
        if (file.Length == 0)
            return BadRequest(new { error = "Empty file" });

        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            // MissingFieldFound = null — silently ignore missing optional columns
        };

        // IFormFile.OpenReadStream() returns the uploaded byte stream.
        // We read it directly — no need to save to disk first.
        using var reader = new StreamReader(file.OpenReadStream());
        using var csv = new CsvReader(reader, config);

        var records = new List<TransactionCsvRecord>();
        await foreach (var record in csv.GetRecordsAsync<TransactionCsvRecord>(ct))
            records.Add(record);

        logger.LogInformation("Imported {Count} CSV records from '{Name}'.", records.Count, file.FileName);

        return Ok(new
        {
            fileName = file.FileName,
            sizeBytes = file.Length,
            recordCount = records.Count,
            records
        });
    }

    // GET /files/csv/export — produce a CSV download from in-memory data
    [HttpGet("export")]
    public IActionResult ExportCsv()
    {
        var records = new List<TransactionCsvRecord>
        {
            new() { Date = "2025-01-15", AccountId = "ACC001", Amount = 1500m, Description = "Salary" },
            new() { Date = "2025-01-16", AccountId = "ACC001", Amount = -40m,  Description = "Groceries" },
            new() { Date = "2025-01-17", AccountId = "ACC002", Amount = 200m,  Description = "Refund" },
        };

        var config = new CsvConfiguration(CultureInfo.InvariantCulture) { HasHeaderRecord = true };
        var ms = new MemoryStream();
        using var writer = new StreamWriter(ms, leaveOpen: true);
        using var csv = new CsvWriter(writer, config);
        csv.WriteRecords(records);
        writer.Flush();
        ms.Position = 0;

        return File(ms, "text/csv", "transactions.csv");
    }

    // POST /files/json/save — serialise a payload to a JSON file
    [HttpPost("/files/json/save")]
    public async Task<IActionResult> SaveJson(
        [FromBody] JsonDocument payload, CancellationToken ct)
    {
        var path = Path.Combine(Path.GetTempPath(), $"data_{Guid.NewGuid()}.json");

        // JsonSerializer.SerializeAsync writes directly to a stream — no temp string
        await using var fs = new FileStream(path, FileMode.Create, FileAccess.Write,
            FileShare.None, bufferSize: 4096, useAsync: true);
        await JsonSerializer.SerializeAsync(fs, payload, cancellationToken: ct);

        return Ok(new { path });
    }

    // GET /files/json/load?path=... — deserialise a JSON file
    [HttpGet("/files/json/load")]
    public async Task<IActionResult> LoadJson([FromQuery] string path, CancellationToken ct)
    {
        if (!System.IO.File.Exists(path))
            return NotFound();

        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.Read, bufferSize: 4096, useAsync: true);

        // DeserializeAsync reads from a stream asynchronously
        var doc = await JsonSerializer.DeserializeAsync<JsonElement>(fs, cancellationToken: ct);
        return Ok(doc);
    }
}
