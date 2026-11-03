using StockQuoteAlert.Core.Application.Journal;
using StockQuoteAlert.Core.Domain;
using StockQuoteAlert.Tests.TestDoubles;

namespace StockQuoteAlert.Tests.Core.Application.Journal;

public sealed class JournalRecordsTests
{
    [Fact]
    public void QuoteObservation_ExposesValuesAndComparesByValue()
    {
        var quote = TestData.QuoteAt(22.70m);
        var observation = new QuoteObservation(quote, PriceZone.AboveSellThreshold, TestData.Now.AddSeconds(1));

        Assert.Equal(quote, observation.Quote);
        Assert.Equal(PriceZone.AboveSellThreshold, observation.Zone);
        Assert.Equal(TestData.Now.AddSeconds(1), observation.ObservedAt);
        Assert.Equal(observation, observation with { });
        Assert.NotEqual(observation, observation with { Zone = PriceZone.Neutral });
    }

    [Fact]
    public void NotificationRecord_ExposesValuesWithoutErrorByDefault()
    {
        var id = Guid.CreateVersion7();
        var record = new NotificationRecord(id, TestData.SellAlert(), "E-mail", "dest@gmail.com", "subject", "body", TestData.Now, NotificationStatus.Sent);

        Assert.Equal(id, record.Id);
        Assert.Equal(TestData.SellAlert(), record.Alert);
        Assert.Equal("E-mail", record.Channel);
        Assert.Equal("dest@gmail.com", record.Recipient);
        Assert.Equal("subject", record.Subject);
        Assert.Equal("body", record.Body);
        Assert.Equal(TestData.Now, record.AttemptedAt);
        Assert.Equal(NotificationStatus.Sent, record.Status);
        Assert.Null(record.Error);
        Assert.NotEqual(record, record with { Status = NotificationStatus.Failed, Error = "boom" });
    }

    [Theory]
    [InlineData(MonitoringFailureKind.Transient, true)]
    [InlineData(MonitoringFailureKind.TickerNotFound, false)]
    [InlineData(MonitoringFailureKind.Rejected, false)]
    public void MonitoringFailure_IsRecoverableOnlyWhenTransient(MonitoringFailureKind kind, bool recoverable)
    {
        var failure = new MonitoringFailure(TestData.Petr4, TestData.Now, "reason", kind);

        Assert.Equal(recoverable, failure.IsRecoverable);
        Assert.Equal(TestData.Petr4, failure.Ticker);
        Assert.Equal(TestData.Now, failure.OccurredAt);
        Assert.Equal("reason", failure.Reason);
        Assert.Equal(kind, failure.Kind);
    }

    [Fact]
    public void MonitoringSnapshot_ExposesItsLists()
    {
        var observation = new QuoteObservation(TestData.QuoteAt(22.63m), PriceZone.Neutral, TestData.Now);
        var failure = new MonitoringFailure(TestData.Petr4, TestData.Now, "reason", MonitoringFailureKind.Transient);

        var snapshot = new MonitoringSnapshot([observation], [], [failure]);

        Assert.Equal([observation], snapshot.Quotes);
        Assert.Empty(snapshot.Notifications);
        Assert.Equal([failure], snapshot.LastFailures);
    }

    [Fact]
    public void MonitoringEvents_WrapWhatWasRecorded()
    {
        var observation = new QuoteObservation(TestData.QuoteAt(22.63m), PriceZone.Neutral, TestData.Now);
        var notification = new NotificationRecord(Guid.CreateVersion7(), TestData.BuyAlert(), "E-mail", "dest@gmail.com", "s", "b", TestData.Now, NotificationStatus.Failed, "boom");
        var failure = new MonitoringFailure(TestData.Petr4, TestData.Now, "reason", MonitoringFailureKind.Rejected);

        Assert.Equal(observation, new MonitoringEvent.QuoteObserved(observation).Observation);
        Assert.Equal(notification, new MonitoringEvent.NotificationAttempted(notification).Notification);
        Assert.Equal(failure, new MonitoringEvent.FailureOccurred(failure).Failure);
        Assert.Equal(new MonitoringEvent.FailureOccurred(failure), new MonitoringEvent.FailureOccurred(failure));
    }
}