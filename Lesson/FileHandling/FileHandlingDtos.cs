namespace Lesson.FileHandling;

/// <summary>Request DTOs for Lesson 10-A file-handling endpoints.</summary>

public record TransactionLine(string Date, string AccountId, decimal Amount, string Description);

public record ExportRequest(List<TransactionLine> Transactions);

public record AppendRequest(string Path, string Line);

public record BinaryRequest(string Base64Data);
