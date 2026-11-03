using StockQuoteAlert.Cli.Components.Dashboard;
using StockQuoteAlert.Core.Application.Journal;
using StockQuoteAlert.Core.Domain;
using StockQuoteAlert.Tests.TestDoubles;

namespace StockQuoteAlert.Tests.Cli.Components;

public sealed class TickerDashboardTests
{
    [Fact]
    public void Create_SplitsTheSnapshotByStockInTargetOrder()
    {
        var petr = Observation(TestData.Petr4, 22.63m, 0);
        var vale = Observation(TestData.Vale3, 60.96m, 1);
        var petrAlert = Notification(TestData.SellAlert(), NotificationStatus.Sent);
        var snapshot = new MonitoringSnapshot([petr, vale], [petrAlert], []);

        var tickers = TickerDashboard.Create(TestData.PetrAndVale, snapshot);

        Assert.Equal(["PETR4", "VALE3"], tickers.Select(ticker => ticker.Symbol));
        Assert.Equal([petr], tickers[0].Quotes);
        Assert.Equal([vale], tickers[1].Quotes);
        Assert.Equal([petrAlert], tickers[0].Notifications);
        Assert.Empty(tickers[1].Notifications);
    }

    [Fact]
    public void Create_IgnoresStocksThatAreNotMonitored()
    {
        var snapshot = new MonitoringSnapshot([Observation(TestData.Vale3, 60.96m, 0)], [], []);

        Assert.Empty(Assert.Single(TickerDashboard.Create(TestData.Targets, snapshot)).Quotes);
    }

    [Fact]
    public void LastQuote_IsTheNewestOrNull()
    {
        var older = Observation(TestData.Petr4, 22.63m, 0);
        var newer = Observation(TestData.Petr4, 22.64m, 30);

        Assert.Null(new TickerDashboard(TestData.Target, [], [], null).LastQuote);
        Assert.Equal(newer, new TickerDashboard(TestData.Target, [older, newer], [], null).LastQuote);
    }

    [Fact]
    public void SentAndFailed_CountTheNotifications()
    {
        var ticker = new TickerDashboard(
            TestData.Target,
            [],
            [Notification(TestData.SellAlert(), NotificationStatus.Sent), Notification(TestData.BuyAlert(), NotificationStatus.Failed), Notification(TestData.SellAlert(), NotificationStatus.Sent)],
            null);

        Assert.Equal(2, ticker.Sent);
        Assert.Equal(1, ticker.Failed);
    }

    [Fact]
    public void TransientFailure_IsActiveUntilANewerQuoteArrives()
    {
        var failure = Failure(MonitoringFailureKind.Transient, 10);

        Assert.Equal(failure, TickerDashboard.Active(failure, null));
        Assert.Equal(failure, TickerDashboard.Active(failure, Observation(TestData.Petr4, 22.63m, 5)));
        Assert.Null(TickerDashboard.Active(failure, Observation(TestData.Petr4, 22.63m, 10)));
        Assert.Null(TickerDashboard.Active(failure, Observation(TestData.Petr4, 22.63m, 40)));
    }

    [Theory]
    [InlineData(MonitoringFailureKind.TickerNotFound)]
    [InlineData(MonitoringFailureKind.Rejected)]
    public void UnrecoverableFailure_StaysActiveEvenAfterNewerQuotes(MonitoringFailureKind kind)
    {
        var failure = Failure(kind, 10);

        Assert.Equal(failure, TickerDashboard.Active(failure, Observation(TestData.Petr4, 22.63m, 40)));
    }

    [Fact]
    public void Create_UsesTheActiveFailureAndHealth()
    {
        var snapshot = new MonitoringSnapshot([Observation(TestData.Petr4, 22.63m, 0)], [], [Failure(MonitoringFailureKind.Transient, 10)]);

        var ticker = Assert.Single(TickerDashboard.Create(TestData.Targets, snapshot));

        Assert.NotNull(ticker.ActiveFailure);
        Assert.Equal(MonitoringHealth.Unstable, ticker.Health);
    }

    [Fact]
    public void For_BuildsTheSameDataAsCreateForOneStock()
    {
        var snapshot = new MonitoringSnapshot(
            [Observation(TestData.Petr4, 22.63m, 0), Observation(TestData.Vale3, 60.96m, 1)],
            [Notification(TestData.SellAlert(), NotificationStatus.Sent)],
            [new MonitoringFailure(TestData.Vale3, TestData.Now.AddSeconds(10), "reason", MonitoringFailureKind.Transient)]);

        var created = TickerDashboard.Create(TestData.PetrAndVale, snapshot);

        Assert.Equal(created[0].Quotes, TickerDashboard.For(TestData.Target, snapshot).Quotes);
        Assert.Equal(created[0].Notifications, TickerDashboard.For(TestData.Target, snapshot).Notifications);
        Assert.Equal(created[1].ActiveFailure, TickerDashboard.For(TestData.ValeTarget, snapshot).ActiveFailure);
        Assert.Null(TickerDashboard.For(TestData.Target, snapshot).ActiveFailure);
    }

    [Fact]
    public void For_WithNullArguments_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => TickerDashboard.For(null!, new MonitoringSnapshot([], [], [])));
        Assert.Throws<ArgumentNullException>(() => TickerDashboard.For(TestData.Target, null!));
    }

    [Fact]
    public void Create_WithNullArguments_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => TickerDashboard.Create(null!, new MonitoringSnapshot([], [], [])));
        Assert.Throws<ArgumentNullException>(() => TickerDashboard.Create(TestData.Targets, null!));
    }

    private static QuoteObservation Observation(Ticker ticker, decimal price, int seconds) =>
        new(new Quote(ticker, price, TestData.Now, "Yahoo Finance"), PriceZone.Neutral, TestData.Now.AddSeconds(seconds));

    private static NotificationRecord Notification(PriceAlert alert, NotificationStatus status) =>
        new(Guid.CreateVersion7(), alert, "E-mail", "dest@gmail.com", "subject", "body", TestData.Now, status);

    private static MonitoringFailure Failure(MonitoringFailureKind kind, int seconds) =>
        new(TestData.Petr4, TestData.Now.AddSeconds(seconds), "reason", kind);
}