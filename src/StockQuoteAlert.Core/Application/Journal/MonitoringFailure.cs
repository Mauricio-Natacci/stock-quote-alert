using StockQuoteAlert.Core.Domain;

namespace StockQuoteAlert.Core.Application.Journal;

/// <param name="Reason">Technical description of the failure, as reported by the provider.</param>
public sealed record MonitoringFailure(Ticker Ticker, DateTimeOffset OccurredAt, string Reason, MonitoringFailureKind Kind)
{
    public bool IsRecoverable => Kind == MonitoringFailureKind.Transient;
}