using System.Net;
using MailKit.Net.Smtp;
using Microsoft.Extensions.Logging;
using StockQuoteAlert.Core.Application.Exceptions;
using StockQuoteAlert.Core.Application.Monitoring;
using StockQuoteAlert.Core.Domain;
using StockQuoteAlert.Tests.TestDoubles;

namespace StockQuoteAlert.Tests.Core.Application.Monitoring;

/// <summary>PETR4 with sell above 22.67 and buy below 22.59, through the real object graph.</summary>
public sealed class StockMonitorTests : IDisposable
{
    private readonly MonitoringHarness _harness = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private StockMonitor Monitor => _harness.Monitor;

    private List<string> SentSubjects => [.. _harness.Smtp.Delivered.Select(email => email.Subject)];

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
    public async Task CheckAsync_PriceBetweenOrOnReferences_SendsNothing(decimal price)
    {
        await _harness.CheckPricesAsync(price);

        Assert.Empty(_harness.Smtp.Delivered);
        Assert.Equal(PriceZone.Neutral, Monitor.CurrentZone);
    }

    [Fact]
    public async Task CheckAsync_PriceAboveSell_SendsSellAlert()
    {
        await _harness.CheckPricesAsync(22.70m);

        Assert.Equal(["[VENDA] PETR4 subiu para R$ 22,70, 0,13% acima da referência de venda"], SentSubjects);
        Assert.Equal("dest@gmail.com", Assert.Single(_harness.Smtp.Delivered).To);
        Assert.Equal(PriceZone.AboveSellThreshold, Monitor.CurrentZone);
    }

    [Fact]
    public async Task CheckAsync_PriceBelowBuy_SendsBuyAlert()
    {
        await _harness.CheckPricesAsync(22.50m);

        Assert.Equal(["[COMPRA] PETR4 caiu para R$ 22,50, 0,40% abaixo da referência de compra"], SentSubjects);
        Assert.Equal(PriceZone.BelowBuyThreshold, Monitor.CurrentZone);
    }

    [Fact]
    public async Task CheckAsync_PriceStaysAboveSell_SendsOnlyOneAlert()
    {
        await _harness.CheckPricesAsync(22.70m, 22.80m, 22.75m, 23m);

        Assert.Single(_harness.Smtp.Delivered);
        Assert.Equal(4, _harness.QuoteApi.Requests.Count);
    }

    [Fact]
    public async Task CheckAsync_PriceStaysBelowBuy_SendsOnlyOneAlert()
    {
        await _harness.CheckPricesAsync(22.50m, 22.40m, 22.55m);

        Assert.Single(_harness.Smtp.Delivered);
    }

    [Fact]
    public async Task CheckAsync_PriceLeavesAndReentersSellZone_SendsAgain()
    {
        await _harness.CheckPricesAsync(22.70m, 22.63m, 22.72m);

        Assert.Equal(2, SentSubjects.Count);
        Assert.All(SentSubjects, subject => Assert.StartsWith("[VENDA]", subject, StringComparison.Ordinal));
        Assert.Contains("R$ 22,72", SentSubjects[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckAsync_PriceTouchingTheReferenceAfterAlert_CountsAsLeavingTheZone()
    {
        await _harness.CheckPricesAsync(22.70m, 22.67m, 22.70m);

        Assert.Equal(2, _harness.Smtp.Delivered.Count);
    }

    [Fact]
    public async Task CheckAsync_PriceJumpsFromSellToBuyZone_SendsBothAlerts()
    {
        await _harness.CheckPricesAsync(22.70m, 22.50m);

        Assert.Collection(
            SentSubjects,
            subject => Assert.StartsWith("[VENDA]", subject, StringComparison.Ordinal),
            subject => Assert.StartsWith("[COMPRA]", subject, StringComparison.Ordinal));
        Assert.Equal(PriceZone.BelowBuyThreshold, Monitor.CurrentZone);
    }

    [Fact]
    public async Task CheckAsync_PriceBackBetweenReferences_ReturnsToNeutralWithoutEmail()
    {
        await _harness.CheckPricesAsync(22.70m, 22.63m);

        Assert.Single(_harness.Smtp.Delivered);
        Assert.Equal(PriceZone.Neutral, Monitor.CurrentZone);
    }

    [Fact]
    public async Task CheckAsync_WhenDeliveryFails_KeepsZoneAndRetriesOnNextCheck()
    {
        _harness.Smtp.ConnectionFailure = new IOException("Connection refused");
        await _harness.CheckPricesAsync(22.70m);

        Assert.Empty(_harness.Smtp.Delivered);
        Assert.Equal(PriceZone.Neutral, Monitor.CurrentZone);

        _harness.Smtp.ConnectionFailure = null;
        await _harness.CheckPricesAsync(22.75m);

        Assert.Equal(["[VENDA] PETR4 subiu para R$ 22,75, 0,35% acima da referência de venda"], SentSubjects);
        Assert.Equal(PriceZone.AboveSellThreshold, Monitor.CurrentZone);
    }

    [Fact]
    public async Task CheckAsync_WhenDeliveryKeepsFailing_RetriesOnEveryCheck()
    {
        _harness.Smtp.SendFailure = new SmtpCommandException(SmtpErrorCode.MessageNotAccepted, SmtpStatusCode.TransactionFailed, "554 rejected");

        await _harness.CheckPricesAsync(22.70m, 22.71m, 22.72m);

        Assert.Equal(3, _harness.Smtp.Clients.Count);
        Assert.Empty(_harness.Smtp.Delivered);
        Assert.Equal(PriceZone.Neutral, Monitor.CurrentZone);
    }

    [Fact]
    public async Task CheckAsync_WhenDeliveryFailsAndPriceReturnsToNeutral_DropsTheAlert()
    {
        _harness.Smtp.ConnectionFailure = new IOException("Connection refused");
        await _harness.CheckPricesAsync(22.70m);

        _harness.Smtp.ConnectionFailure = null;
        await _harness.CheckPricesAsync(22.63m);

        Assert.Single(_harness.Smtp.Clients);
        Assert.Empty(_harness.Smtp.Delivered);
        Assert.Equal(PriceZone.Neutral, Monitor.CurrentZone);
    }

    [Fact]
    public async Task CheckAsync_WhenBuyDeliveryFailsAfterSellAlert_KeepsSellZoneAndRetriesBuy()
    {
        await _harness.CheckPricesAsync(22.70m);
        _harness.Smtp.ConnectionFailure = new IOException("Connection refused");
        await _harness.CheckPricesAsync(22.50m);

        Assert.Equal(PriceZone.AboveSellThreshold, Monitor.CurrentZone);

        _harness.Smtp.ConnectionFailure = null;
        await _harness.CheckPricesAsync(22.50m);

        Assert.Equal(2, _harness.Smtp.Delivered.Count);
        Assert.StartsWith("[COMPRA]", SentSubjects[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckAsync_WhenQuoteFails_PropagatesAndKeepsZone()
    {
        await _harness.CheckPricesAsync(22.70m);
        _harness.QuoteApi.RespondWithStatus(HttpStatusCode.ServiceUnavailable);

        var exception = await Assert.ThrowsAsync<QuoteProviderException>(() => Monitor.CheckAsync(Ct));

        Assert.True(exception.IsRecoverable);
        Assert.Equal(PriceZone.AboveSellThreshold, Monitor.CurrentZone);
        Assert.Single(_harness.Smtp.Delivered);
    }

    [Fact]
    public async Task CheckAsync_AfterQuoteFailure_ContinuesOnNextCheck()
    {
        _harness.QuoteApi.RespondWithStatus(HttpStatusCode.ServiceUnavailable);
        await Assert.ThrowsAsync<QuoteProviderException>(() => Monitor.CheckAsync(Ct));

        await _harness.CheckPricesAsync(22.70m);

        Assert.Single(_harness.Smtp.Delivered);
    }

    [Fact]
    public async Task CheckAsync_WhenTickerIsUnknown_PropagatesTickerNotFound()
    {
        _harness.QuoteApi.RespondWithStatus(HttpStatusCode.NotFound);

        var exception = await Assert.ThrowsAsync<TickerNotFoundException>(() => Monitor.CheckAsync(Ct));

        Assert.False(exception.IsRecoverable);
        Assert.Empty(_harness.Smtp.Clients);
    }

    [Fact]
    public async Task CheckAsync_WhenCancelled_PropagatesAndKeepsZone()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Monitor.CheckAsync(cancellation.Token));

        Assert.Equal(PriceZone.Neutral, Monitor.CurrentZone);
        Assert.Empty(_harness.Smtp.Clients);
    }

    [Fact]
    public async Task CheckAsync_WhenCancelledDuringDelivery_PropagatesAndKeepsZone()
    {
        using var cancellation = new CancellationTokenSource();
        _harness.QuoteApi.RespondWithPrices(22.70m);
        _harness.Smtp.ConnectionFailure = new OperationCanceledException(cancellation.Token);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Monitor.CheckAsync(cancellation.Token));

        Assert.Equal(PriceZone.Neutral, Monitor.CurrentZone);
    }

    [Fact]
    public async Task CheckAsync_LogsEveryQuote()
    {
        await _harness.CheckPricesAsync(22.63m, 22.70m);

        var quotes = _harness.MonitorLog.Entries.Where(entry => entry.Message.Contains(" quoted at ", StringComparison.Ordinal)).ToList();
        Assert.Equal(
            ["PETR4 quoted at 22.63 by Yahoo Finance (Neutral)", "PETR4 quoted at 22.70 by Yahoo Finance (AboveSellThreshold)"],
            quotes.Select(entry => entry.Message));
        Assert.All(quotes, entry => Assert.Equal(LogLevel.Information, entry.Level));
    }

    [Fact]
    public async Task CheckAsync_LogsSentAlertAndReturnToNeutral()
    {
        await _harness.CheckPricesAsync(22.70m, 22.63m);

        var messages = _harness.MonitorLog.Entries.Select(entry => entry.Message).ToList();
        Assert.Contains("Sell alert sent for PETR4 at 22.70", messages);
        Assert.Contains("PETR4 is back between thresholds at 22.63", messages);
    }

    [Fact]
    public async Task CheckAsync_WhenDeliveryFails_LogsWarningAboutRetry()
    {
        _harness.Smtp.ConnectionFailure = new IOException("Connection refused");

        await _harness.CheckPricesAsync(22.50m);

        var warning = Assert.Single(_harness.MonitorLog.Entries, entry => entry.Level == LogLevel.Warning);
        Assert.Equal("Buy alert for PETR4 at 22.50 was not delivered; it will be retried on the next check", warning.Message);
    }

    [Fact]
    public async Task CheckAsync_QuoteLog_IsStructured()
    {
        await _harness.CheckPricesAsync(22.7049m);

        var entry = Assert.Single(_harness.MonitorLog.Entries, entry => entry.Message.Contains(" quoted at ", StringComparison.Ordinal));
        Assert.Equal("PETR4", entry.Properties["Ticker"]);
        Assert.Equal(22.70m, entry.Properties["Price"]);
        Assert.Equal("Yahoo Finance", entry.Properties["Source"]);
        Assert.Equal(PriceZone.AboveSellThreshold, entry.Properties["Zone"]);
    }

    [Theory]
    [InlineData(22.7, "22.70")]
    [InlineData(1234.5, "1234.50")]
    [InlineData(23, "23.00")]
    public async Task CheckAsync_LogsPricesWithTwoDecimals(decimal price, string expected)
    {
        await _harness.CheckPricesAsync(price);

        Assert.Contains(_harness.MonitorLog.Entries, entry => entry.Message == $"PETR4 quoted at {expected} by Yahoo Finance (AboveSellThreshold)");
    }

    [Fact]
    public async Task CheckAsync_AlertLogs_AreStructured()
    {
        _harness.Smtp.ConnectionFailure = new IOException("Connection refused");
        await _harness.CheckPricesAsync(22.50m);
        _harness.Smtp.ConnectionFailure = null;
        await _harness.CheckPricesAsync(22.50m, 22.63m);

        var notDelivered = Assert.Single(_harness.MonitorLog.Entries, entry => entry.Level == LogLevel.Warning);
        var sent = Assert.Single(_harness.MonitorLog.Entries, entry => entry.Message.StartsWith("Buy alert sent", StringComparison.Ordinal));
        var neutral = Assert.Single(_harness.MonitorLog.Entries, entry => entry.Message.Contains("back between thresholds", StringComparison.Ordinal));
        Assert.All([notDelivered, sent], entry =>
        {
            Assert.Equal(AlertKind.Buy, entry.Properties["Kind"]);
            Assert.Equal("PETR4", entry.Properties["Ticker"]);
            Assert.Equal(22.50m, entry.Properties["Price"]);
        });
        Assert.Equal(22.63m, neutral.Properties["Price"]);
    }

    [Fact]
    public async Task CheckAsync_UsesTheTargetThresholds()
    {
        using var harness = new MonitoringHarness(new MonitoringTarget(Ticker.Create("VALE3"), new AlertThresholds(61.50m, 60.80m)));

        await harness.CheckPricesAsync(22.70m);

        Assert.Equal(["[COMPRA] VALE3 caiu para R$ 22,70, 62,66% abaixo da referência de compra"], harness.Smtp.Delivered.Select(email => email.Subject));
        Assert.EndsWith("/chart/VALE3.SA", Assert.Single(harness.QuoteApi.Requests).Uri.AbsolutePath, StringComparison.Ordinal);
    }

    public void Dispose() => _harness.Dispose();
}