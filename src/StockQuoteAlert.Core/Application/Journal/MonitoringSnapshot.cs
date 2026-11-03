namespace StockQuoteAlert.Core.Application.Journal;

/// <param name="Quotes">Observations of every stock, oldest first.</param>
/// <param name="Notifications">Delivery attempts, oldest first.</param>
/// <param name="LastFailures">The most recent failure of each stock that has failed, oldest first.</param>
public sealed record MonitoringSnapshot(
    IReadOnlyList<QuoteObservation> Quotes,
    IReadOnlyList<NotificationRecord> Notifications,
    IReadOnlyList<MonitoringFailure> LastFailures);