namespace StockQuoteAlert.Cli.Dashboard;

internal sealed record MonitoringConfigurationResponse(
    IReadOnlyList<TargetResponse> Targets,
    string QuoteProvider,
    string? FallbackQuoteProvider,
    double PollingIntervalSeconds,
    string Recipient,
    DateTimeOffset StartedAt);