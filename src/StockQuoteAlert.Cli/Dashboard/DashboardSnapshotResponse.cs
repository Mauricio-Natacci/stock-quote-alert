namespace StockQuoteAlert.Cli.Dashboard;

/// <summary>Body of <c>GET /api/dashboard</c>: what is monitored and everything the journal holds.</summary>
internal sealed record DashboardSnapshotResponse(
    MonitoringConfigurationResponse Configuration,
    IReadOnlyList<QuoteResponse> Quotes,
    IReadOnlyList<NotificationResponse> Notifications,
    IReadOnlyList<FailureResponse> LastFailures);