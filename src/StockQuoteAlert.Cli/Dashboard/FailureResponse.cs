using StockQuoteAlert.Core.Application.Journal;

namespace StockQuoteAlert.Cli.Dashboard;

internal sealed record FailureResponse(string Ticker, DateTimeOffset OccurredAt, string Reason, MonitoringFailureKind Kind, bool IsRecoverable)
{
    public static FailureResponse From(MonitoringFailure failure) =>
        new(failure.Ticker.Symbol, failure.OccurredAt, failure.Reason, failure.Kind, failure.IsRecoverable);
}