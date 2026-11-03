using System.ComponentModel.DataAnnotations;

namespace StockQuoteAlert.Core.Application.Monitoring;

public sealed class MonitoringOptions
{
    public const string SectionName = "Monitoring";

    /// <summary>Time between checks. The first check runs as soon as monitoring starts.</summary>
    [Range(typeof(TimeSpan), "00:00:05", "1.00:00:00", ErrorMessage = $"{SectionName}:{nameof(PollingInterval)} must be between 5 seconds and 1 day.")]
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(30);
}