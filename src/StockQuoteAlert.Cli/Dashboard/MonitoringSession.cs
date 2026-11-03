namespace StockQuoteAlert.Cli.Dashboard;

/// <param name="StartedAt">When the app was built, by the configured clock.</param>
internal sealed record MonitoringSession(DateTimeOffset StartedAt);