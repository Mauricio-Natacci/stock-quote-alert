using StockQuoteAlert.Core.Application.Journal;
using StockQuoteAlert.Core.Domain;
using StockQuoteAlert.Infrastructure.Journal;
using StockQuoteAlert.Tests.TestDoubles;

namespace StockQuoteAlert.Tests.Infrastructure.Journal;

public sealed class InMemoryMonitoringJournalTests
{
    private static readonly TimeSpan SignalTimeout = TimeSpan.FromSeconds(5);

    private readonly InMemoryMonitoringJournal _journal = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void NewJournal_HasAnEmptySnapshot()
    {
        var snapshot = _journal.GetSnapshot();

        Assert.Empty(snapshot.Quotes);
        Assert.Empty(snapshot.Notifications);
        Assert.Empty(snapshot.LastFailures);
    }

    [Fact]
    public void RecordQuote_IsInTheSnapshot()
    {
        var observation = Observation(TestData.Petr4, 22.63m, seconds: 0);

        _journal.RecordQuote(observation);

        Assert.Equal([observation], _journal.GetSnapshot().Quotes);
    }

    [Fact]
    public void Snapshot_ListsQuotesOfEveryStockOldestFirst()
    {
        _journal.RecordQuote(Observation(TestData.Petr4, 22.63m, seconds: 0));
        _journal.RecordQuote(Observation(TestData.Vale3, 60.96m, seconds: 1));
        _journal.RecordQuote(Observation(TestData.Petr4, 22.64m, seconds: 30));
        _journal.RecordQuote(Observation(TestData.Vale3, 60.97m, seconds: 31));

        Assert.Equal(
            [("PETR4", 22.63m), ("VALE3", 60.96m), ("PETR4", 22.64m), ("VALE3", 60.97m)],
            _journal.GetSnapshot().Quotes.Select(quote => (quote.Quote.Ticker.Symbol, quote.Quote.Price)));
    }

    [Fact]
    public void Quotes_AreBoundedPerStockKeepingTheNewest()
    {
        var capacity = InMemoryMonitoringJournal.QuoteCapacityPerTicker;

        for (var i = 0; i < capacity + 5; i++)
        {
            _journal.RecordQuote(Observation(TestData.Petr4, 20m + (i * 0.01m), seconds: i));
        }

        _journal.RecordQuote(Observation(TestData.Vale3, 60.96m, seconds: 0));

        var quotes = _journal.GetSnapshot().Quotes;
        var petr4 = quotes.Where(quote => quote.Quote.Ticker == TestData.Petr4).ToList();
        Assert.Equal(capacity, petr4.Count);
        Assert.Equal(20m + (5 * 0.01m), petr4[0].Quote.Price);
        Assert.Single(quotes, quote => quote.Quote.Ticker == TestData.Vale3);
    }

    [Fact]
    public void Notifications_AreKeptOldestFirstAndBounded()
    {
        var capacity = InMemoryMonitoringJournal.NotificationCapacity;

        for (var i = 0; i < capacity + 3; i++)
        {
            _journal.RecordNotification(Notification($"subject {i}"));
        }

        var notifications = _journal.GetSnapshot().Notifications;
        Assert.Equal(capacity, notifications.Count);
        Assert.Equal("subject 3", notifications[0].Subject);
        Assert.Equal($"subject {capacity + 2}", notifications[^1].Subject);
    }

    [Fact]
    public void RecordFailure_KeepsOnlyTheLastFailurePerStock()
    {
        _journal.RecordFailure(Failure(TestData.Petr4, "first", seconds: 0));
        _journal.RecordFailure(Failure(TestData.Vale3, "vale", seconds: 1));
        _journal.RecordFailure(Failure(TestData.Petr4, "second", seconds: 2));

        Assert.Equal(
            [("VALE3", "vale"), ("PETR4", "second")],
            _journal.GetSnapshot().LastFailures.Select(failure => (failure.Ticker.Symbol, failure.Reason)));
    }

    [Fact]
    public void Snapshot_IsACopy()
    {
        var snapshot = _journal.GetSnapshot();

        _journal.RecordQuote(Observation(TestData.Petr4, 22.63m, seconds: 0));
        _journal.RecordNotification(Notification("subject"));
        _journal.RecordFailure(Failure(TestData.Petr4, "reason", seconds: 0));

        Assert.Empty(snapshot.Quotes);
        Assert.Empty(snapshot.Notifications);
        Assert.Empty(snapshot.LastFailures);
    }

    [Fact]
    public void Record_WithNull_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => _journal.RecordQuote(null!));
        Assert.Throws<ArgumentNullException>(() => _journal.RecordNotification(null!));
        Assert.Throws<ArgumentNullException>(() => _journal.RecordFailure(null!));
    }

    [Fact]
    public async Task Subscriber_ReceivesEveryRecordInOrder()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        await using var events = _journal.SubscribeAsync(cancellation.Token).GetAsyncEnumerator(cancellation.Token);
        var first = events.MoveNextAsync();
        await Eventually.HoldsAsync(() => _journal.SubscriberCount == 1);

        var observation = Observation(TestData.Petr4, 22.63m, seconds: 0);
        var notification = Notification("subject");
        var failure = Failure(TestData.Petr4, "reason", seconds: 1);
        _journal.RecordQuote(observation);
        _journal.RecordNotification(notification);
        _journal.RecordFailure(failure);

        Assert.True(await first.AsTask().WaitAsync(SignalTimeout, Ct));
        Assert.Equal(new MonitoringEvent.QuoteObserved(observation), events.Current);
        Assert.True(await events.MoveNextAsync());
        Assert.Equal(new MonitoringEvent.NotificationAttempted(notification), events.Current);
        Assert.True(await events.MoveNextAsync());
        Assert.Equal(new MonitoringEvent.FailureOccurred(failure), events.Current);
    }

    [Fact]
    public async Task Subscriber_DoesNotReceiveWhatWasRecordedBefore()
    {
        _journal.RecordQuote(Observation(TestData.Petr4, 22.63m, seconds: 0));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        await using var events = _journal.SubscribeAsync(cancellation.Token).GetAsyncEnumerator(cancellation.Token);
        var next = events.MoveNextAsync();
        await Eventually.HoldsAsync(() => _journal.SubscriberCount == 1);

        var later = Observation(TestData.Petr4, 22.64m, seconds: 30);
        _journal.RecordQuote(later);

        Assert.True(await next.AsTask().WaitAsync(SignalTimeout, Ct));
        Assert.Equal(new MonitoringEvent.QuoteObserved(later), events.Current);
    }

    [Fact]
    public async Task EverySubscriber_ReceivesEveryEvent()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        await using var first = _journal.SubscribeAsync(cancellation.Token).GetAsyncEnumerator(cancellation.Token);
        await using var second = _journal.SubscribeAsync(cancellation.Token).GetAsyncEnumerator(cancellation.Token);
        var firstNext = first.MoveNextAsync();
        var secondNext = second.MoveNextAsync();
        await Eventually.HoldsAsync(() => _journal.SubscriberCount == 2);

        var failure = Failure(TestData.Petr4, "reason", seconds: 0);
        _journal.RecordFailure(failure);

        Assert.True(await firstNext.AsTask().WaitAsync(SignalTimeout, Ct));
        Assert.True(await secondNext.AsTask().WaitAsync(SignalTimeout, Ct));
        Assert.Equal(new MonitoringEvent.FailureOccurred(failure), first.Current);
        Assert.Equal(new MonitoringEvent.FailureOccurred(failure), second.Current);
    }

    [Fact]
    public async Task CancellingTheSubscription_RemovesTheSubscriber()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var reading = Task.Run(async () =>
        {
            await foreach (var _ in _journal.SubscribeAsync(cancellation.Token))
            {
            }
        }, Ct);
        await Eventually.HoldsAsync(() => _journal.SubscriberCount == 1);

        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reading);
        Assert.Equal(0, _journal.SubscriberCount);
    }

    [Fact]
    public async Task SlowSubscriber_LosesItsOldestEventsWithoutBlockingRecording()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        await using var events = _journal.SubscribeAsync(cancellation.Token).GetAsyncEnumerator(cancellation.Token);
        var next = events.MoveNextAsync();
        await Eventually.HoldsAsync(() => _journal.SubscriberCount == 1);

        // Recording returns right away even though nobody is reading.
        var total = InMemoryMonitoringJournal.SubscriberBufferSize + 50;
        for (var i = 0; i < total; i++)
        {
            _journal.RecordNotification(Notification($"subject {i}"));
        }

        var received = new List<int>();
        Assert.True(await next.AsTask().WaitAsync(SignalTimeout, Ct));
        received.Add(Number(events.Current));
        while (received[^1] < total - 1)
        {
            Assert.True(await events.MoveNextAsync().AsTask().WaitAsync(SignalTimeout, Ct));
            received.Add(Number(events.Current));
        }

        // The pending read may or may not catch the first event; after it, only the newest events are kept, in order.
        Assert.True(received.Count <= InMemoryMonitoringJournal.SubscriberBufferSize + 1);
        var newest = received.Skip(received.Count - InMemoryMonitoringJournal.SubscriberBufferSize).ToList();
        Assert.Equal(Enumerable.Range(total - InMemoryMonitoringJournal.SubscriberBufferSize, InMemoryMonitoringJournal.SubscriberBufferSize), newest);
        Assert.Equal(total, _journal.GetSnapshot().Notifications.Count);
    }

    private static int Number(MonitoringEvent monitoringEvent) =>
        int.Parse(Assert.IsType<MonitoringEvent.NotificationAttempted>(monitoringEvent).Notification.Subject["subject ".Length..], System.Globalization.CultureInfo.InvariantCulture);

    private static QuoteObservation Observation(Ticker ticker, decimal price, int seconds) =>
        new(new Quote(ticker, price, TestData.Now, "Yahoo Finance"), PriceZone.Neutral, TestData.Now.AddSeconds(seconds));

    private static NotificationRecord Notification(string subject) =>
        new(Guid.CreateVersion7(), TestData.SellAlert(), "E-mail", "dest@gmail.com", subject, "body", TestData.Now, NotificationStatus.Sent);

    private static MonitoringFailure Failure(Ticker ticker, string reason, int seconds) =>
        new(ticker, TestData.Now.AddSeconds(seconds), reason, MonitoringFailureKind.Transient);
}