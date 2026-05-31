using CsvHelper.Configuration.Attributes;

namespace Lesson.FileHandling;

/// <summary>
/// Lesson 10-B — CsvHelper class map record.
/// Each property maps to a CSV column by name (case-insensitive by default).
/// </summary>
public record TransactionCsvRecord
{
    [Name("date")]        public string Date        { get; init; } = "";
    [Name("account_id")]  public string AccountId   { get; init; } = "";
    [Name("amount")]      public decimal Amount     { get; init; }
    [Name("description")] public string Description { get; init; } = "";
}
