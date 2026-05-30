using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Lesson.Data;

namespace Lesson.Tests;

/// <summary>
/// Lesson 10-A integration tests — File, StreamReader/StreamWriter, FileStream, FileInfo.
///
/// Strategy: call the /files/* endpoints, verify responses, and clean up temp files
/// created during the test run.
/// </summary>
public class FileHandlingBasicTests : IClassFixture<FileHandlingTestFactory>
{
    private readonly HttpClient _client;

    public FileHandlingBasicTests(FileHandlingTestFactory factory) =>
        _client = factory.CreateClient();

    // ── Export → Read roundtrip ───────────────────────────────────────────────

    [Fact]
    public async Task Export_Returns200_WithPathAndLineCount()
    {
        var request = new
        {
            transactions = new[]
            {
                new { date = "2025-01-15", accountId = "ACC001", amount = 150.00m, description = "Salary" },
                new { date = "2025-01-16", accountId = "ACC001", amount = -40.00m, description = "Groceries" }
            }
        };

        var response = await _client.PostAsJsonAsync("/files/export", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ExportResult>();
        Assert.NotNull(body?.path);
        Assert.Equal(2, body!.lines);

        // cleanup
        if (File.Exists(body.path)) File.Delete(body.path);
    }

    [Fact]
    public async Task Export_ThenRead_ReturnsMatchingLines()
    {
        var request = new
        {
            transactions = new[]
            {
                new { date = "2025-02-01", accountId = "ACC002", amount = 500m, description = "Transfer" }
            }
        };

        var exportResponse = await _client.PostAsJsonAsync("/files/export", request);
        var exported = await exportResponse.Content.ReadFromJsonAsync<ExportResult>();
        Assert.NotNull(exported?.path);

        var readResponse = await _client.GetAsync($"/files/read?path={Uri.EscapeDataString(exported!.path)}");
        Assert.Equal(HttpStatusCode.OK, readResponse.StatusCode);

        var read = await readResponse.Content.ReadFromJsonAsync<ReadResult>();
        Assert.Single(read!.lines);
        Assert.Contains("ACC002", read.lines[0]);

        if (File.Exists(exported.path)) File.Delete(exported.path);
    }

    // ── Read missing file ─────────────────────────────────────────────────────

    [Fact]
    public async Task Read_MissingFile_Returns404()
    {
        var response = await _client.GetAsync("/files/read?path=/nonexistent/file.txt");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── Append ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Append_AddsLineToFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"append_test_{Guid.NewGuid()}.txt");
        await File.WriteAllTextAsync(path, "original\n");

        var appendReq = new { path, line = "appended line" };
        var response = await _client.PostAsJsonAsync("/files/append", appendReq);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await File.ReadAllTextAsync(path);
        Assert.Contains("appended line", content);

        File.Delete(path);
    }

    // ── File info ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Info_ReturnsFileMetadata()
    {
        var path = Path.Combine(Path.GetTempPath(), $"info_test_{Guid.NewGuid()}.txt");
        await File.WriteAllTextAsync(path, "hello");

        var response = await _client.GetAsync($"/files/info?path={Uri.EscapeDataString(path)}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var info = await response.Content.ReadFromJsonAsync<FileInfoResult>();
        Assert.Equal(Path.GetFileName(path), info!.name);
        Assert.True(info.sizeBytes > 0);

        File.Delete(path);
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_RemovesFile_Returns204()
    {
        var path = Path.Combine(Path.GetTempPath(), $"delete_test_{Guid.NewGuid()}.txt");
        await File.WriteAllTextAsync(path, "to delete");

        var response = await _client.DeleteAsync($"/files/delete?path={Uri.EscapeDataString(path)}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task Delete_MissingFile_Returns404()
    {
        var response = await _client.DeleteAsync("/files/delete?path=/no/such/file.txt");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── Binary FileStream ─────────────────────────────────────────────────────

    [Fact]
    public async Task WriteBinary_Returns200_WithBytesWritten()
    {
        var data = Convert.ToBase64String(new byte[] { 0x50, 0x44, 0x46 }); // "PDF"
        var response = await _client.PostAsJsonAsync("/files/binary", new { base64Data = data });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<BinaryResult>();
        Assert.Equal(3, result!.bytesWritten);
        if (File.Exists(result.path)) File.Delete(result.path);
    }

    // ── Response shapes ───────────────────────────────────────────────────────
    private record ExportResult(string path, int lines);
    private record ReadResult(string path, string[] lines);
    private record FileInfoResult(string name, long sizeBytes, DateTime created, DateTime modified);
    private record BinaryResult(string path, int bytesWritten);
}

public class FileHandlingTestFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection;

    public FileHandlingTestFactory()
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
