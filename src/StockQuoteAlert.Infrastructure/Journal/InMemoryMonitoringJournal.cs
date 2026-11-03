using System.Runtime.CompilerServices;
using System.Threading.Channels;
using StockQuoteAlert.Core.Application.Abstractions;
using StockQuoteAlert.Core.Application.Journal;
using StockQuoteAlert.Core.Domain;

namespace StockQuoteAlert.Infrastructure.Journal;

/// <summary>
/// Keeps recent history in memory. Quotes are bounded per stock, so monitoring more stocks does not shorten the
/// history of each one. Every subscriber gets its own bounded channel: a slow reader loses its oldest events instead
/// of slowing down monitoring.
/// </summary>
internal sealed class InMemoryMonitoringJournal : IMonitoringJournal
{
    internal const int QuoteCapacityPerTicker = 2_000;
    internal const int NotificationCapacity = 500;
    internal const int SubscriberBufferSize = 256;

    private readonly Lock _gate = new();
    private readonly Dictionary<Ticker, LinkedList<QuoteObservation>> _quotes = [];
    private readonly LinkedList<NotificationRecord> _notifications = new();
    private readonly Dictionary<Ticker, MonitoringFailure> _lastFailures = [];
    private readonly List<ChannelWriter<MonitoringEvent>> _subscribers = [];

    internal int SubscriberCount
    {
        get
        {
            lock (_gate)
            {
                return _subscribers.Count;
            }
        }
    }

    public void RecordQuote(QuoteObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);

        lock (_gate)
        {
            var ticker = observation.Quote.Ticker;

            if (!_quotes.TryGetValue(ticker, out var quotes))
            {
                _quotes[ticker] = quotes = new LinkedList<QuoteObservation>();
            }

            AppendBounded(quotes, observation, QuoteCapacityPerTicker);
            Publish(new MonitoringEvent.QuoteObserved(observation));
        }
    }

    public void RecordNotification(NotificationRecord notification)
    {
        ArgumentNullException.ThrowIfNull(notification);

        lock (_gate)
        {
            AppendBounded(_notifications, notification, NotificationCapacity);
            Publish(new MonitoringEvent.NotificationAttempted(notification));
        }
    }

    public void RecordFailure(MonitoringFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        lock (_gate)
        {
            _lastFailures[failure.Ticker] = failure;
            Publish(new MonitoringEvent.FailureOccurred(failure));
        }
    }

    public MonitoringSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            return new MonitoringSnapshot(
                [.. _quotes.Values.SelectMany(quotes => quotes).OrderBy(quote => quote.ObservedAt)],
                [.. _notifications],
                [.. _lastFailures.Values.OrderBy(failure => failure.OccurredAt)]);
        }
    }

    public async IAsyncEnumerable<MonitoringEvent> SubscribeAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var channel = Channel.CreateBounded<MonitoringEvent>(new BoundedChannelOptions(SubscriberBufferSize)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });

        lock (_gate)
        {
            _subscribers.Add(channel.Writer);
        }

        try
        {
            await foreach (var monitoringEvent in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return monitoringEvent;
            }
        }
        finally
        {
            lock (_gate)
            {
                _subscribers.Remove(channel.Writer);
            }
        }
    }

    private static void AppendBounded<T>(LinkedList<T> list, T item, int capacity)
    {
        list.AddLast(item);

        if (list.Count > capacity)
        {
            list.RemoveFirst();
        }
    }

    // Called under the lock; TryWrite never blocks because full channels drop their oldest event.
    private void Publish(MonitoringEvent monitoringEvent)
    {
        foreach (var subscriber in _subscribers)
        {
            subscriber.TryWrite(monitoringEvent);
        }
    }
}