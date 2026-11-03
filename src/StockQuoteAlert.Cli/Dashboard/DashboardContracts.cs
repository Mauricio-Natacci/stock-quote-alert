using StockQuoteAlert.Core.Application.Journal;
using StockQuoteAlert.Core.Application.Monitoring;
using StockQuoteAlert.Core.Domain;

namespace StockQuoteAlert.Cli.Dashboard;

/// <summary>Body of <c>GET /api/dashboard</c>: what is monitored and everything the journal holds.</summary>
internal sealed record DashboardSnapshotResponse(
    MonitoringConfigurationResponse Configuration,
    IReadOnlyList<QuoteResponse> Quotes,
    IReadOnlyList<NotificationResponse> Notifications,
    IReadOnlyList<FailureResponse> LastFailures);

internal sealed record MonitoringConfigurationResponse(
    IReadOnlyList<TargetResponse> Targets,
    string QuoteProvider,
    string? FallbackQuoteProvider,
    double PollingIntervalSeconds,
    string Recipient,
    DateTimeOffset StartedAt);

internal sealed record TargetResponse(string Ticker, decimal SellAbove, decimal BuyBelow)
{
    public static TargetResponse From(MonitoringTarget target) =>
        new(target.Ticker.Symbol, target.Thresholds.SellAbove, target.Thresholds.BuyBelow);
}

internal sealed record QuoteResponse(string Ticker, decimal Price, PriceZone Zone, string Source, DateTimeOffset QuotedAt, DateTimeOffset ObservedAt)
{
    public static QuoteResponse From(QuoteObservation observation) => new(
        observation.Quote.Ticker.Symbol,
        observation.Quote.Price,
        observation.Zone,
        observation.Quote.Source,
        observation.Quote.QuotedAt,
        observation.ObservedAt);
}

internal sealed record NotificationResponse(
    Guid Id,
    string Ticker,
    AlertKind Action,
    decimal Price,
    decimal ReferencePrice,
    DateTimeOffset QuotedAt,
    string Channel,
    string Recipient,
    string Subject,
    string Body,
    DateTimeOffset AttemptedAt,
    NotificationStatus Status,
    string? Error)
{
    public static NotificationResponse From(NotificationRecord record) => new(
        record.Id,
        record.Alert.Ticker.Symbol,
        record.Alert.Kind,
        record.Alert.CurrentPrice,
        record.Alert.ReferencePrice,
        record.Alert.QuotedAt,
        record.Channel,
        record.Recipient,
        record.Subject,
        record.Body,
        record.AttemptedAt,
        record.Status,
        record.Error);
}

internal sealed record FailureResponse(string Ticker, DateTimeOffset OccurredAt, string Reason, MonitoringFailureKind Kind, bool IsRecoverable)
{
    public static FailureResponse From(MonitoringFailure failure) =>
        new(failure.Ticker.Symbol, failure.OccurredAt, failure.Reason, failure.Kind, failure.IsRecoverable);
}