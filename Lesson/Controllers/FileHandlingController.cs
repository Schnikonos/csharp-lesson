using Lesson.FileHandling;
using Microsoft.AspNetCore.Mvc;

namespace Lesson.Controllers;

/// <summary>
/// Lesson 10-A — File system operations using File/Directory helpers,
/// StreamReader/StreamWriter, FileStream, and using declarations.
///
/// Domain scenario: exporting and importing banking transaction logs as plain text.
/// </summary>
[ApiController]
[Route("files")]
public class FileHandlingController(ILogger<FileHandlingController> logger) : ControllerBase
{
    // POST /files/export — write transactions to a text file
    [HttpPost("export")]
    public async Task<IActionResult> ExportTransactions(
        [FromBody] ExportRequest request, CancellationToken ct)
    {
        var path = Path.Combine(Path.GetTempPath(), $"transactions_{DateTime.UtcNow:yyyyMMddHHmmss}.txt");

        // StreamWriter with 'await using' — async dispose flushes buffers
        // Java parallel: try-with-resources PrintWriter(new FileWriter(path))
        await using var writer = new StreamWriter(path, append: false);

        foreach (var tx in request.Transactions)
        {
            var line = $"{tx.Date:yyyy-MM-dd},{tx.AccountId},{tx.Amount:F2},{tx.Description}";
            await writer.WriteLineAsync(line.AsMemory(), ct);
        }

        logger.LogInformation("Exported {Count} transactions to {Path}", request.Transactions.Count, path);

        return Ok(new { path, lines = request.Transactions.Count });
    }

    // GET /files/read?path=... — read a previously exported file
    [HttpGet("read")]
    public async Task<IActionResult> ReadFile([FromQuery] string path, CancellationToken ct)
    {
        if (!System.IO.File.Exists(path))
            return NotFound(new { error = "File not found", path });

        // StreamReader with 'using' — synchronous dispose (StreamReader is not IAsyncDisposable)
        // Java parallel: try-with-resources BufferedReader(new FileReader(path))
        var lines = new List<string>();
        using var reader = new StreamReader(path);

        while (await reader.ReadLineAsync(ct) is { } line)
            lines.Add(line);

        return Ok(new { path, lines });
    }

    // POST /files/append — append a single line to an existing file
    [HttpPost("append")]
    public async Task<IActionResult> AppendLine(
        [FromBody] AppendRequest request, CancellationToken ct)
    {
        // File.AppendText returns a StreamWriter opened for append
        await using var writer = System.IO.File.AppendText(request.Path);
        await writer.WriteLineAsync(request.Line.AsMemory(), ct);
        return Ok(new { appended = true });
    }

    // GET /files/info?path=... — file metadata using static File/FileInfo helpers
    [HttpGet("info")]
    public IActionResult GetFileInfo([FromQuery] string path)
    {
        if (!System.IO.File.Exists(path))
            return NotFound();

        var info = new FileInfo(path);
        return Ok(new
        {
            name = info.Name,
            sizeBytes = info.Length,
            created = info.CreationTimeUtc,
            modified = info.LastWriteTimeUtc,
            directory = info.DirectoryName
        });
    }

    // DELETE /files/delete?path=... — remove a file
    [HttpDelete("delete")]
    public IActionResult DeleteFile([FromQuery] string path)
    {
        if (!System.IO.File.Exists(path))
            return NotFound();

        System.IO.File.Delete(path);
        return NoContent();
    }

    // POST /files/binary — write binary data with FileStream
    [HttpPost("binary")]
    public async Task<IActionResult> WriteBinary(
        [FromBody] BinaryRequest request, CancellationToken ct)
    {
        var path = Path.Combine(Path.GetTempPath(), $"report_{Guid.NewGuid()}.bin");

        // FileStream with explicit mode/access flags — full control over buffering
        // Java parallel: new FileOutputStream(path) wrapped in BufferedOutputStream
        await using var fs = new FileStream(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            useAsync: true);

        var bytes = Convert.FromBase64String(request.Base64Data);
        await fs.WriteAsync(bytes, ct);

        return Ok(new { path, bytesWritten = bytes.Length });
    }
}
