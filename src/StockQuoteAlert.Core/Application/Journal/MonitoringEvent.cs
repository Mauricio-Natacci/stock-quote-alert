namespace StockQuoteAlert.Core.Application.Journal;

/// <summary>Something recorded in the journal, as published to live subscribers.</summary>
public abstract record MonitoringEvent
{
    private MonitoringEvent()
    {
    }

    public sealed record QuoteObserved(QuoteObservation Observation) : MonitoringEvent;

    public sealed record NotificationAttempted(NotificationRecord Notification) : MonitoringEvent;

    public sealed record FailureOccurred(MonitoringFailure Failure) : MonitoringEvent;
}