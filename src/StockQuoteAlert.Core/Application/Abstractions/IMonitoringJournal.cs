using StockQuoteAlert.Core.Application.Journal;

namespace StockQuoteAlert.Core.Application.Abstractions;

/// <summary>Recent monitoring history (quotes, notifications, failures) and a live feed of what happens next.</summary>
public interface IMonitoringJournal
{
    void RecordQuote(QuoteObservation observation);

    void RecordNotification(NotificationRecord notification);

    void RecordFailure(MonitoringFailure failure);

    MonitoringSnapshot GetSnapshot();

    /// <summary>
    /// Streams every event recorded after the enumeration starts, until <paramref name="cancellationToken"/> is
    /// cancelled. Pair it with <see cref="GetSnapshot"/> for what happened before.
    /// </summary>
    IAsyncEnumerable<MonitoringEvent> SubscribeAsync(CancellationToken cancellationToken);
}