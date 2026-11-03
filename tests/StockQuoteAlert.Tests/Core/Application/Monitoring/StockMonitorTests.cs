using System.Net;
using Microsoft.Extensions.Logging;
using StockQuoteAlert.Core.Application.Exceptions;
using StockQuoteAlert.Core.Application.Monitoring;
using StockQuoteAlert.Core.Domain;
using StockQuoteAlert.Tests.TestDoubles;

namespace StockQuoteAlert.Tests.Core.Application.Monitoring;

/// <summary>PETR4 with sell above 22.67 and buy below 22.59, through the real object graph; alerts are read from the queue.</summary>
public sealed class StockMonitorTests : IDisposable
{
    private readonly MonitoringHarness _harness = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private StockMonitor Monitor => _harness.Monitor;

    private List<(AlertKind Kind, decimal Price)> Alerts => [.. _harness.Published.Select(raised => (raised.Alert.Kind, raised.Alert.CurrentPrice))];

    [Fact]
    public void Target_IsTheMonitoredTarget()
    {
        Assert.Equal(TestData.Target, Monitor.Target);
    }

    [Fact]
    public void CurrentZone_StartsNeutral()
    {
        Assert.Equal(PriceZone.Neutral, Monitor.CurrentZone);
    }

    [Fact]
    public async Task CheckAsync_FetchesTheTargetTicker()
    {
        await _harness.CheckPricesAsync(22.63m);

        Assert.EndsWith("/chart/PETR4.SA", Assert.Single(_harness.QuoteApi.Requests).Uri.AbsolutePath, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(22.63)]
    [InlineData(22.67)]
    [InlineData(22.59)]
    public async Task CheckAsync_PriceBetweenOrOnReferences_PublishesNothing(decimal price)
    {
        await _harness.CheckPricesAsync(price);

        Assert.Empty(_harness.Published);
        Assert.Equal(PriceZone.Neutral, Monitor.CurrentZone);
    }

    [Fact]
    public async Task CheckAsync_PriceAboveSell_PublishesASellAlert()
    {
        await _harness.CheckPricesAsync(22.70m);

        var raised = Assert.Single(_harness.Published);
        Assert.Equal(new PriceAlert(TestData.Petr4, AlertKind.Sell, 22.70m, 22.67m, TestData.Now), raised.Alert);
        Assert.Equal(TestData.Now, raised.RaisedAt);
        Assert.NotEqual(Guid.Empty, raised.Id);
        Assert.Equal(PriceZone.AboveSellThreshold, Monitor.CurrentZone);
    }

    [Fact]
    public async Task CheckAsync_PriceBelowBuy_PublishesABuyAlert()
    {
        await _harness.CheckPricesAsync(22.50m);

        Assert.Equal([(AlertKind.Buy, 22.50m)], Alerts);
        Assert.Equal(22.59m, _harness.Published[0].Alert.ReferencePrice);
        Assert.Equal(PriceZone.BelowBuyThreshold, Monitor.CurrentZone);
    }

    [Fact]
    public async Task CheckAsync_DoesNotWaitForDelivery()
    {
        // With the SMTP server down the alert is still published at once; nothing is sent until the dispatcher runs.
        _harness.Smtp.ConnectionFailure = new IOException("Connection refused");

        await _harness.CheckPricesAsync(22.70m);

        Assert.Single(_harness.Published);
        Assert.Empty(_harness.Smtp.Clients);
        Assert.Equal(PriceZone.AboveSellThreshold, Monitor.CurrentZone);
    }

    [Fact]
    public async Task CheckAsync_PriceStaysAboveSell_PublishesOnlyOneAlert()
    {
        await _harness.CheckPricesAsync(22.70m, 22.80m, 22.75m, 23m);

        Assert.Single(_harness.Published);
        Assert.Equal(4, _harness.QuoteApi.Requests.Count);
    }

    [Fact]
    public async Task CheckAsync_PriceStaysBelowBuy_PublishesOnlyOneAlert()
    {
        await _harness.CheckPricesAsync(22.50m, 22.40m, 22.55m);

        Assert.Single(_harness.Published);
    }

    [Fact]
    public async Task CheckAsync_PriceLeavesAndReentersSellZone_PublishesAgain()
    {
        await _harness.CheckPricesAsync(22.70m, 22.63m, 22.72m);

        Assert.Equal([(AlertKind.Sell, 22.70m), (AlertKind.Sell, 22.72m)], Alerts);
    }

    [Fact]
    public async Task CheckAsync_PriceTouchingTheReferenceAfterAlert_CountsAsLeavingTheZone()
    {
        await _harness.CheckPricesAsync(22.70m, 22.67m, 22.70m);

        Assert.Equal(2, _harness.Published.Count);
    }

    [Fact]
    public async Task CheckAsync_PriceJumpsFromSellToBuyZone_PublishesBothAlerts()
    {
        await _harness.CheckPricesAsync(22.70m, 22.50m);

        Assert.Equal([(AlertKind.Sell, 22.70m), (AlertKind.Buy, 22.50m)], Alerts);
        Assert.Equal(PriceZone.BelowBuyThreshold, Monitor.CurrentZone);
    }

    [Fact]
    public async Task CheckAsync_PriceBackBetweenReferences_ReturnsToNeutralWithoutAlert()
    {
        await _harness.CheckPricesAsync(22.70m, 22.63m);

        Assert.Single(_harness.Published);
        Assert.Equal(PriceZone.Neutral, Monitor.CurrentZone);
    }

    [Fact]
    public async Task CheckAsync_EveryAlertHasItsOwnId()
    {
        await _harness.CheckPricesAsync(22.70m, 22.50m);

        Assert.NotEqual(_harness.Published[0].Id, _harness.Published[1].Id);
    }

    [Fact]
    public async Task CheckAsync_WhenQuoteFails_PropagatesAndKeepsZone()
    {
        await _harness.CheckPricesAsync(22.70m);
        _harness.QuoteApi.RespondWithStatus(HttpStatusCode.ServiceUnavailable);

        var exception = await Assert.ThrowsAsync<QuoteProviderException>(() => Monitor.CheckAsync(Ct));

        Assert.True(exception.IsRecoverable);
        Assert.Equal(PriceZone.AboveSellThreshold, Monitor.CurrentZone);
        Assert.Single(_harness.Published);
    }

    [Fact]
    public async Task CheckAsync_AfterQuoteFailure_ContinuesOnNextCheck()
    {
        _harness.QuoteApi.RespondWithStatus(HttpStatusCode.ServiceUnavailable);
        await Assert.ThrowsAsync<QuoteProviderException>(() => Monitor.CheckAsync(Ct));

        await _harness.CheckPricesAsync(22.70m);

        Assert.Single(_harness.Published);
    }

    [Fact]
    public async Task CheckAsync_WhenTickerIsUnknown_PropagatesTickerNotFound()
    {
        _harness.QuoteApi.RespondWithStatus(HttpStatusCode.NotFound);

        var exception = await Assert.ThrowsAsync<TickerNotFoundException>(() => Monitor.CheckAsync(Ct));

        Assert.False(exception.IsRecoverable);
        Assert.Empty(_harness.Published);
    }

    [Fact]
    public async Task CheckAsync_WhenCancelled_PropagatesAndKeepsZone()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Monitor.CheckAsync(cancellation.Token));

        Assert.Equal(PriceZone.Neutral, Monitor.CurrentZone);
        Assert.Empty(_harness.Published);
    }

    [Fact]
    public async Task CheckAsync_RecordsEveryQuoteWithItsZoneAndObservationTime()
    {
        await _harness.CheckPricesAsync(22.63m);
        _harness.Time.Advance(TimeSpan.FromSeconds(30));
        await _harness.CheckPricesAsync(22.70m);

        var quotes = _harness.Journal.GetSnapshot().Quotes;
        Assert.Equal(
            [(22.63m, PriceZone.Neutral, TestData.Now), (22.70m, PriceZone.AboveSellThreshold, TestData.Now.AddSeconds(30))],
            quotes.Select(quote => (quote.Quote.Price, quote.Zone, quote.ObservedAt)));
        Assert.All(quotes, quote => Assert.Equal("Yahoo Finance", quote.Quote.Source));
    }

    [Fact]
    public async Task CheckAsync_WhenQuoteFails_RecordsNoQuote()
    {
        _harness.QuoteApi.RespondWithStatus(HttpStatusCode.ServiceUnavailable);

        await Assert.ThrowsAsync<QuoteProviderException>(() => Monitor.CheckAsync(Ct));

        Assert.Empty(_harness.Journal.GetSnapshot().Quotes);
    }

    [Fact]
    public async Task CheckAsync_LogsEveryQuote()
    {
        await _harness.CheckPricesAsync(22.63m, 22.70m);

        var quotes = _harness.MonitorLog.Entries.Where(entry => entry.Message.Contains(" quoted at ", StringComparison.Ordinal)).ToList();
        Assert.Equal(
            ["PETR4 quoted at 22.63 by Yahoo Finance (neutral)", "PETR4 quoted at 22.70 by Yahoo Finance (above sell 22.67)"],
            quotes.Select(entry => entry.Message));
        Assert.All(quotes, entry => Assert.Equal(LogLevel.Information, entry.Level));
    }

    [Fact]
    public async Task CheckAsync_QuoteLog_IsStructured()
    {
        await _harness.CheckPricesAsync(22.7049m);

        var entry = Assert.Single(_harness.MonitorLog.Entries, entry => entry.Message.Contains(" quoted at ", StringComparison.Ordinal));
        Assert.Equal("PETR4", entry.Properties["Ticker"]);
        Assert.Equal(22.70m, entry.Properties["Price"]);
        Assert.Equal("Yahoo Finance", entry.Properties["Source"]);
        Assert.Equal("above sell 22.67", entry.Properties["Position"]);
    }

    [Fact]
    public async Task CheckAsync_QuoteLog_SaysWhereThePriceStandsInPlainWords()
    {
        await _harness.CheckPricesAsync(22.63m, 22.50m);

        Assert.Equal(
            ["PETR4 quoted at 22.63 by Yahoo Finance (neutral)", "PETR4 quoted at 22.50 by Yahoo Finance (below buy 22.59)"],
            _harness.MonitorLog.Entries.Where(entry => entry.Message.Contains(" quoted at ", StringComparison.Ordinal)).Select(entry => entry.Message));
    }

    [Theory]
    [InlineData(22.7, "22.70")]
    [InlineData(1234.5, "1234.50")]
    [InlineData(23, "23.00")]
    public async Task CheckAsync_LogsPricesWithTwoDecimals(decimal price, string expected)
    {
        await _harness.CheckPricesAsync(price);

        Assert.Contains(_harness.MonitorLog.Entries, entry => entry.Message == $"PETR4 quoted at {expected} by Yahoo Finance (above sell 22.67)");
    }

    [Fact]
    public async Task CheckAsync_LogsRaisedAlertsAndReturnToNeutral()
    {
        await _harness.CheckPricesAsync(22.70m, 22.63m);

        var messages = _harness.MonitorLog.Entries.Select(entry => entry.Message).ToList();
        Assert.Contains("Sell alert raised for PETR4 at 22.70", messages);
        Assert.Contains("PETR4 is back between thresholds at 22.63", messages);
    }

    [Fact]
    public async Task CheckAsync_AlertLogs_AreStructured()
    {
        await _harness.CheckPricesAsync(22.50m, 22.63m);

        var raised = Assert.Single(_harness.MonitorLog.Entries, entry => entry.Message.StartsWith("Buy alert raised", StringComparison.Ordinal));
        var neutral = Assert.Single(_harness.MonitorLog.Entries, entry => entry.Message.Contains("back between thresholds", StringComparison.Ordinal));
        Assert.Equal(AlertKind.Buy, raised.Properties["Kind"]);
        Assert.Equal("PETR4", raised.Properties["Ticker"]);
        Assert.Equal(22.50m, raised.Properties["Price"]);
        Assert.Equal(22.63m, neutral.Properties["Price"]);
    }

    [Fact]
    public async Task CheckAsync_UsesTheTargetThresholds()
    {
        using var harness = new MonitoringHarness(new MonitoringTarget(Ticker.Create("VALE3"), new AlertThresholds(61.50m, 60.80m)));

        await harness.CheckPricesAsync(22.70m);

        var raised = Assert.Single(harness.Published);
        Assert.Equal(new PriceAlert(TestData.Vale3, AlertKind.Buy, 22.70m, 60.80m, TestData.Now), raised.Alert);
        Assert.EndsWith("/chart/VALE3.SA", Assert.Single(harness.QuoteApi.Requests).Uri.AbsolutePath, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(PriceZone.AboveSellThreshold, "above sell 22.67")]
    [InlineData(PriceZone.BelowBuyThreshold, "below buy 22.59")]
    [InlineData(PriceZone.Neutral, "neutral")]
    public void Position_NamesTheZoneAndTheCrossedReference(PriceZone zone, string expected)
    {
        Assert.Equal(expected, StockMonitor.Position(zone, TestData.Thresholds));
    }

    [Fact]
    public void Position_ShowsWholeReferencesWithTwoDecimals()
    {
        Assert.Equal("above sell 1000.00", StockMonitor.Position(PriceZone.AboveSellThreshold, new AlertThresholds(1000m, 1m)));
    }

    public void Dispose() => _harness.Dispose();
}