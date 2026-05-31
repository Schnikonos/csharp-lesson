using System.ComponentModel.DataAnnotations;

namespace Lesson.Options;

/// <summary>
/// Used to demonstrate Named Options: the same POCO registered under multiple names.
///
/// Registration:
///   services.AddOptions&lt;TransferLimitOptions&gt;("domestic")
///       .BindConfiguration("TransferLimits:Domestic");
///   services.AddOptions&lt;TransferLimitOptions&gt;("international")
///       .BindConfiguration("TransferLimits:International");
///
/// Retrieval via IOptionsMonitor&lt;TransferLimitOptions&gt;:
///   monitor.Get("domestic")
///   monitor.Get("international")
///
/// Java parallel: Spring's @Qualifier — multiple beans of the same type distinguished by name.
/// </summary>
public sealed class TransferLimitOptions
{
    [Range(1, double.MaxValue)]
    public decimal DailyLimit { get; init; }

    [Range(1, double.MaxValue)]
    public decimal SingleTransactionLimit { get; init; }

    public string Currency { get; init; } = "USD";
}
