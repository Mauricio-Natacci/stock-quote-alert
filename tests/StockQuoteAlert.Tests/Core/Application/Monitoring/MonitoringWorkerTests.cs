using System.Globalization;
using System.Net;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using StockQuoteAlert.Core.Application.Abstractions;
using StockQuoteAlert.Core.Application.Exceptions;
using StockQuoteAlert.Core.Application.Journal;
using StockQuoteAlert.Core.Application.Monitoring;
using StockQuoteAlert.Core.Domain;
using StockQuoteAlert.Tests.TestDoubles;

namespace StockQuoteAlert.Tests.Core.Application.Monitoring;

/// <summary>Drives the worker with a fake clock; every check goes through the real monitoring graph.</summary>
public sealed class MonitoringWorkerTests : IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan SignalTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan NoRequestWindow = TimeSpan.FromMilliseconds(200);

    private readonly MonitoringHarness _harness = new();
    private readonly IHostApplicationLifetime _lifetime = Substitute.For<IHostApplicationLifetime>();
    private readonly MonitoringWorker _worker;

    public MonitoringWorkerTests()
    {
        _worker = _harness.CreateWorker(_lifetime, Interval);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private bool StopRequested => _lifetime.ReceivedCalls().Any(call => call.GetMethodInfo().Name == nameof(IHostApplicationLifetime.StopApplication));

    [Fact]
    public async Task Start_ChecksRightAway()
    {
        await _worker.StartAsync(Ct);

        await WaitForCheckAsync();
        await _worker.StopAsync(Ct);
        Assert.Single(_harness.QuoteApi.Requests);
    }

    [Fact]
    public async Task EachInterval_ChecksAgain()
    {
        await _worker.StartAsync(Ct);
        await WaitForCheckAsync();

        _harness.Time.Advance(Interval);
        await WaitForCheckAsync();

        _harness.Time.Advance(Interval);
        await WaitForCheckAsync();

        await _worker.StopAsync(Ct);
        Assert.Equal(3, _harness.QuoteApi.Requests.Count);
    }

    [Fact]
    public async Task BeforeIntervalElapses_DoesNotCheckAgain()
    {
        await _worker.StartAsync(Ct);
        await WaitForCheckAsync();

        _harness.Time.Advance(Interval - TimeSpan.FromSeconds(1));

        Assert.False(await _harness.QuoteApi.RequestSignal.WaitAsync(NoRequestWindow, Ct));
        _harness.Time.Advance(TimeSpan.FromSeconds(1));
        await WaitForCheckAsync();
        await _worker.StopAsync(Ct);
    }

    [Fact]
    public async Task ConfiguredInterval_IsUsed()
    {
        using var worker = _harness.CreateWorker(_lifetime, TimeSpan.FromSeconds(5));
        await worker.StartAsync(Ct);
        await WaitForCheckAsync();

        _harness.Time.Advance(TimeSpan.FromSeconds(5));

        await WaitForCheckAsync();
        await worker.StopAsync(Ct);
    }

    [Fact]
    public async Task PriceCrossingAReference_PublishesTheAlertOnce()
    {
        _harness.QuoteApi.RespondWithPrices(22.63m, 22.70m, 22.75m);

        await _worker.StartAsync(Ct);
        await WaitForCheckAsync();
        _harness.Time.Advance(Interval);
        await WaitForCheckAsync();
        _harness.Time.Advance(Interval);
        await WaitForCheckAsync();
        await _worker.StopAsync(Ct);
        await _harness.DeliverPendingAlertsAsync();

        Assert.StartsWith("[VENDA] PETR4 subiu para R$ 22,70", Assert.Single(_harness.Smtp.Delivered).Subject, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FailingEmail_DoesNotHoldUpMonitoring()
    {
        // Delivery belongs to the dispatcher: a broken SMTP server never delays the next check.
        _harness.QuoteApi.RespondWithPrices(22.70m, 22.63m);
        _harness.Smtp.ConnectionFailure = new IOException("Connection refused");

        await _worker.StartAsync(Ct);

        // Wait for each check's effect, not its request: the zone starts neutral, so "neutral" alone proves nothing.
        await Eventually.HoldsAsync(() => _harness.Monitor.CurrentZone == PriceZone.AboveSellThreshold);
        _harness.Time.Advance(Interval);
        await Eventually.HoldsAsync(() => _harness.Monitor.CurrentZone == PriceZone.Neutral);
        await _worker.StopAsync(Ct);

        Assert.Single(_harness.Published);
        Assert.Equal(2, _harness.QuoteApi.Requests.Count);
        Assert.Empty(_harness.Smtp.Clients);
    }

    [Fact]
    public async Task RecoverableFailure_IsLoggedAndMonitoringContinues()
    {
        _harness.QuoteApi.RespondWithStatus(HttpStatusCode.ServiceUnavailable).RespondWithPrices(22.70m);

        await _worker.StartAsync(Ct);
        await WaitForCheckAsync();
        await Eventually.HoldsAsync(() => _harness.WorkerLog.Entries.Any(entry => entry.Level == LogLevel.Error));
        _harness.Time.Advance(Interval);
        await Eventually.HoldsAsync(() => _harness.Published.Count == 1);
        await _worker.StopAsync(Ct);

        var error = Assert.Single(_harness.WorkerLog.Entries, entry => entry.Level == LogLevel.Error);
        Assert.Equal("Failed to check PETR4; retrying on next tick", error.Message);
        Assert.IsType<QuoteProviderException>(error.Exception);
        Assert.False(StopRequested);
        Assert.False(_harness.Outcome.NothingLeftToMonitor);
    }

    [Fact]
    public async Task UnexpectedFailure_IsLoggedAndMonitoringContinues()
    {
        _harness.QuoteApi.FailWith(new InvalidOperationException("boom")).RespondWithPrices(22.63m);

        await _worker.StartAsync(Ct);
        await WaitForCheckAsync();
        _harness.Time.Advance(Interval);
        await WaitForCheckAsync();
        await _worker.StopAsync(Ct);

        Assert.Contains(_harness.WorkerLog.Entries, entry => entry.Level == LogLevel.Error);
        Assert.False(StopRequested);
        Assert.False(_harness.Outcome.NothingLeftToMonitor);
    }

    [Fact]
    public async Task UnknownTicker_RecordsUnrecoverableFailureAndStopsTheApplicationWithoutCheckingAgain()
    {
        _harness.QuoteApi.RespondWithStatus(HttpStatusCode.NotFound);

        await _worker.StartAsync(Ct);
        await WaitForCheckAsync();
        await Eventually.HoldsAsync(() => StopRequested);
        _harness.Time.Advance(Interval);

        Assert.False(await _harness.QuoteApi.RequestSignal.WaitAsync(NoRequestWindow, Ct));
        await _worker.StopAsync(Ct);
        Assert.Single(_harness.QuoteApi.Requests);
        Assert.True(_worker.ExecuteTask!.IsCompletedSuccessfully);

        var critical = _harness.WorkerLog.Entries.Where(entry => entry.Level == LogLevel.Critical).ToList();
        Assert.Equal(
            ["Unrecoverable failure while monitoring PETR4; it will no longer be checked", "No stock left to monitor; shutting down"],
            critical.Select(entry => entry.Message));
        Assert.IsType<TickerNotFoundException>(critical[0].Exception);
        Assert.True(_harness.Outcome.NothingLeftToMonitor);
    }

    [Fact]
    public async Task RejectedRequest_RecordsUnrecoverableFailureAndStopsTheApplication()
    {
        using var harness = new MonitoringHarness();
        var quoteProvider = Substitute.For<IQuoteProvider>();
        quoteProvider
            .GetQuoteAsync(Arg.Any<Ticker>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<Quote>(new QuoteProviderException("Token rejected (HTTP 401).", isRecoverable: false)));
        var monitor = new StockMonitor(quoteProvider, Substitute.For<IAlertPublisher>(), harness.Journal, TestData.Target, harness.Time, harness.MonitorLog);
        using var worker = new MonitoringWorker(
            [monitor],
            harness.Journal,
            _lifetime,
            harness.Outcome,
            Options.Create(new MonitoringOptions { PollingInterval = Interval }),
            harness.Time,
            harness.WorkerLog);

        await worker.StartAsync(Ct);
        await Eventually.HoldsAsync(() => StopRequested);
        await worker.StopAsync(Ct);

        Assert.Equal(2, harness.WorkerLog.Entries.Count(entry => entry.Level == LogLevel.Critical));
        Assert.True(harness.Outcome.NothingLeftToMonitor);
        Assert.Single(quoteProvider.ReceivedCalls());
    }

    [Fact]
    public async Task Start_LogsWhatIsMonitored()
    {
        await _worker.StartAsync(Ct);
        await WaitForCheckAsync();
        await _worker.StopAsync(Ct);

        var started = _harness.WorkerLog.Entries.First();
        Assert.Equal(LogLevel.Information, started.Level);
        Assert.Equal("Monitoring PETR4: sell above 22.67, buy below 22.59, every 30 seconds", started.Message);
    }

    [Fact]
    public async Task Start_LogIsStructured()
    {
        using var worker = _harness.CreateWorker(_lifetime, TimeSpan.FromSeconds(90));
        await worker.StartAsync(Ct);
        await WaitForCheckAsync();
        await worker.StopAsync(Ct);

        var started = _harness.WorkerLog.Entries.First();
        Assert.Equal("Monitoring PETR4: sell above 22.67, buy below 22.59, every 1 minute 30 seconds", started.Message);
        Assert.Equal("PETR4", started.Properties["Ticker"]);
        Assert.Equal(22.67m, started.Properties["SellAbove"]);
        Assert.Equal(22.59m, started.Properties["BuyBelow"]);
        Assert.Equal("1 minute 30 seconds", started.Properties["Interval"]);
    }

    [Fact]
    public async Task Stop_CompletesGracefullyAndLogs()
    {
        await _worker.StartAsync(Ct);
        await WaitForCheckAsync();

        await _worker.StopAsync(Ct);

        Assert.True(_worker.ExecuteTask!.IsCompletedSuccessfully);
        Assert.Equal("Stopped monitoring PETR4", _harness.WorkerLog.Entries.Last().Message);
        Assert.False(StopRequested);
        Assert.False(_harness.Outcome.NothingLeftToMonitor);
    }

    [Fact]
    public async Task Stop_DuringASlowCheck_CancelsIt()
    {
        _harness.QuoteApi.RespondSlowly(TimeSpan.FromMinutes(5));

        await _worker.StartAsync(Ct);
        await WaitForCheckAsync();

        await _worker.StopAsync(Ct).WaitAsync(SignalTimeout, Ct);

        Assert.True(_worker.ExecuteTask!.IsCompletedSuccessfully);
        Assert.DoesNotContain(_harness.WorkerLog.Entries, entry => entry.Level >= LogLevel.Error);
    }

    [Fact]
    public async Task SeveralStocks_AreAllCheckedOnEveryTick()
    {
        using var harness = new MonitoringHarness(TestData.Target, TestData.ValeTarget);
        using var worker = harness.CreateWorker(_lifetime, Interval);

        await worker.StartAsync(Ct);
        await Eventually.HoldsAsync(() => harness.QuoteApi.Requests.Count == 2);
        harness.Time.Advance(Interval);
        await Eventually.HoldsAsync(() => harness.QuoteApi.Requests.Count == 4);
        await worker.StopAsync(Ct);

        Assert.Equal(2, harness.QuoteApi.RequestsFor("PETR4"));
        Assert.Equal(2, harness.QuoteApi.RequestsFor("VALE3"));
        Assert.Equal(
            ["Monitoring PETR4: sell above 22.67, buy below 22.59, every 30 seconds", "Monitoring VALE3: sell above 61.50, buy below 60.80, every 30 seconds"],
            harness.WorkerLog.Entries.Where(entry => entry.Message.StartsWith("Monitoring ", StringComparison.Ordinal)).Select(entry => entry.Message));
        Assert.Equal(
            ["Stopped monitoring PETR4", "Stopped monitoring VALE3"],
            harness.WorkerLog.Entries.Where(entry => entry.Message.StartsWith("Stopped ", StringComparison.Ordinal)).Select(entry => entry.Message));
    }

    [Fact]
    public async Task SeveralStocks_AreCheckedInParallel()
    {
        using var harness = new MonitoringHarness(TestData.Target, TestData.ValeTarget);
        harness.QuoteApi.RespondSlowlyFor("PETR4", TimeSpan.FromMinutes(5));
        using var worker = harness.CreateWorker(_lifetime, Interval);

        await worker.StartAsync(Ct);

        // VALE3 is checked while the PETR4 request is still waiting.
        await Eventually.HoldsAsync(() => harness.QuoteApi.RequestsFor("VALE3") == 1 && harness.QuoteApi.RequestsFor("PETR4") == 1);
        await worker.StopAsync(Ct);
    }

    [Fact]
    public async Task SeveralStocksCrossingTogether_SendOneEmailPerStock()
    {
        using var harness = new MonitoringHarness(TestData.Target, TestData.ValeTarget);
        harness.QuoteApi.RespondWithPricesFor("PETR4", 22.70m).RespondWithPricesFor("VALE3", 60.00m);
        using var worker = harness.CreateWorker(_lifetime, Interval);

        await worker.StartAsync(Ct);
        await Eventually.HoldsAsync(() => harness.Published.Count == 2);
        await worker.StopAsync(Ct);
        await harness.DeliverPendingAlertsAsync();

        Assert.Equal(
            ["[COMPRA] VALE3 caiu para R$ 60,00, 1,32% abaixo da referência de compra", "[VENDA] PETR4 subiu para R$ 22,70, 0,13% acima da referência de venda"],
            harness.Smtp.Delivered.Select(email => email.Subject).Order(StringComparer.Ordinal));
        Assert.All(harness.Smtp.Delivered, email =>
        {
            var other = email.Subject.Contains("PETR4", StringComparison.Ordinal) ? "VALE3" : "PETR4";
            Assert.DoesNotContain(other, email.Body, StringComparison.Ordinal);
        });
        Assert.Equal(PriceZone.AboveSellThreshold, harness.Monitors[0].CurrentZone);
        Assert.Equal(PriceZone.BelowBuyThreshold, harness.Monitors[1].CurrentZone);
    }

    [Fact]
    public async Task UnknownTickerAmongOthers_StopsOnlyThatStock()
    {
        using var harness = new MonitoringHarness(TestData.Target, TestData.ValeTarget);
        harness.QuoteApi.RespondWithStatusFor("VALE3", HttpStatusCode.NotFound);
        using var worker = harness.CreateWorker(_lifetime, Interval);

        await worker.StartAsync(Ct);
        await Eventually.HoldsAsync(() => harness.WorkerLog.Entries.Any(entry => entry.Level == LogLevel.Critical));
        harness.Time.Advance(Interval);
        await Eventually.HoldsAsync(() => harness.QuoteApi.RequestsFor("PETR4") == 2);
        harness.Time.Advance(Interval);
        await Eventually.HoldsAsync(() => harness.QuoteApi.RequestsFor("PETR4") == 3);
        await worker.StopAsync(Ct);

        Assert.Equal(1, harness.QuoteApi.RequestsFor("VALE3"));
        var critical = Assert.Single(harness.WorkerLog.Entries, entry => entry.Level == LogLevel.Critical);
        Assert.Equal("Unrecoverable failure while monitoring VALE3; it will no longer be checked", critical.Message);
        Assert.False(StopRequested);
        Assert.False(harness.Outcome.NothingLeftToMonitor);
        Assert.Equal(["Stopped monitoring PETR4"], harness.WorkerLog.Entries.Where(entry => entry.Message.StartsWith("Stopped ", StringComparison.Ordinal)).Select(entry => entry.Message));
    }

    [Fact]
    public async Task EveryTickerUnknown_StopsTheApplicationAndReportsNothingLeft()
    {
        using var harness = new MonitoringHarness(TestData.Target, TestData.ValeTarget);
        harness.QuoteApi.RespondWithStatusFor("PETR4", HttpStatusCode.NotFound).RespondWithStatusFor("VALE3", HttpStatusCode.NotFound);
        using var worker = harness.CreateWorker(_lifetime, Interval);

        await worker.StartAsync(Ct);
        await Eventually.HoldsAsync(() => StopRequested);
        await worker.StopAsync(Ct);

        Assert.True(harness.Outcome.NothingLeftToMonitor);
        Assert.Equal(3, harness.WorkerLog.Entries.Count(entry => entry.Level == LogLevel.Critical));
        Assert.Equal("No stock left to monitor; shutting down", harness.WorkerLog.Entries.Last(entry => entry.Level == LogLevel.Critical).Message);
    }

    [Fact]
    public async Task StocksFailingOnDifferentTicks_StopTheApplicationWhenTheLastOneFails()
    {
        using var harness = new MonitoringHarness(TestData.Target, TestData.ValeTarget);
        harness.QuoteApi
            .RespondWithStatusFor("VALE3", HttpStatusCode.NotFound)
            .RespondWithPricesFor("PETR4", 22.63m)
            .RespondWithStatusFor("PETR4", HttpStatusCode.NotFound);
        using var worker = harness.CreateWorker(_lifetime, Interval);

        await worker.StartAsync(Ct);
        await Eventually.HoldsAsync(() => harness.QuoteApi.Requests.Count == 2);
        Assert.False(StopRequested);

        harness.Time.Advance(Interval);
        await Eventually.HoldsAsync(() => StopRequested);
        await worker.StopAsync(Ct);

        Assert.True(harness.Outcome.NothingLeftToMonitor);
        Assert.Equal(1, harness.QuoteApi.RequestsFor("VALE3"));
        Assert.Equal(2, harness.QuoteApi.RequestsFor("PETR4"));
    }

    [Fact]
    public async Task OneStockFailingTransiently_DoesNotAffectTheOthers()
    {
        using var harness = new MonitoringHarness(TestData.Target, TestData.ValeTarget);
        harness.QuoteApi.RespondWithStatusFor("PETR4", HttpStatusCode.BadRequest).RespondWithPricesFor("VALE3", 60.00m);
        using var worker = harness.CreateWorker(_lifetime, Interval);

        await worker.StartAsync(Ct);
        await Eventually.HoldsAsync(() => harness.Published.Count == 1);
        await worker.StopAsync(Ct);

        Assert.Equal(TestData.Vale3, Assert.Single(harness.Published).Alert.Ticker);
        Assert.Equal("Failed to check PETR4; retrying on next tick", Assert.Single(harness.WorkerLog.Entries, entry => entry.Level == LogLevel.Error).Message);
    }

    [Fact]
    public async Task OneStock_DoesNotLogCheckCompleted()
    {
        await _worker.StartAsync(Ct);
        await WaitForCheckAsync();
        _harness.Time.Advance(Interval);
        await WaitForCheckAsync();
        await _worker.StopAsync(Ct);

        Assert.DoesNotContain(_harness.WorkerLog.Entries, entry => entry.EventName == MonitoringWorker.CheckCompletedEvent);
    }

    [Fact]
    public async Task SeveralStocks_LogCheckCompletedOncePerTick()
    {
        using var harness = new MonitoringHarness(TestData.Target, TestData.ValeTarget);
        using var worker = harness.CreateWorker(_lifetime, Interval);

        await worker.StartAsync(Ct);
        await Eventually.HoldsAsync(() => CheckCompletedCount(harness) == 1);
        harness.Time.Advance(Interval);
        await Eventually.HoldsAsync(() => CheckCompletedCount(harness) == 2);
        await worker.StopAsync(Ct);

        var completed = harness.WorkerLog.Entries.First(entry => entry.EventName == MonitoringWorker.CheckCompletedEvent);
        Assert.Equal(LogLevel.Information, completed.Level);
        Assert.Equal("Check completed", completed.Message);
        Assert.Equal(4, harness.QuoteApi.Requests.Count);
    }

    [Fact]
    public async Task SeveralStocksDownToOne_StopLoggingCheckCompleted()
    {
        using var harness = new MonitoringHarness(TestData.Target, TestData.ValeTarget);
        harness.QuoteApi.RespondWithStatusFor("VALE3", HttpStatusCode.NotFound);
        using var worker = harness.CreateWorker(_lifetime, Interval);

        await worker.StartAsync(Ct);
        await Eventually.HoldsAsync(() => CheckCompletedCount(harness) == 1);
        harness.Time.Advance(Interval);
        await Eventually.HoldsAsync(() => harness.QuoteApi.RequestsFor("PETR4") == 2);
        harness.Time.Advance(Interval);
        await Eventually.HoldsAsync(() => harness.QuoteApi.RequestsFor("PETR4") == 3);
        await worker.StopAsync(Ct);

        Assert.Equal(1, CheckCompletedCount(harness));
    }

    [Fact]
    public async Task TransientFailure_IsRecordedAndMonitoringContinues()
    {
        _harness.QuoteApi.RespondWithStatus(HttpStatusCode.ServiceUnavailable);
        _harness.Time.Advance(TimeSpan.FromSeconds(2));

        await _worker.StartAsync(Ct);
        await Eventually.HoldsAsync(() => _harness.Journal.GetSnapshot().LastFailures.Count == 1);
        _harness.Time.Advance(Interval);
        await Eventually.HoldsAsync(() => _harness.Journal.GetSnapshot().Quotes.Count == 1);
        await _worker.StopAsync(Ct);

        var failure = Assert.Single(_harness.Journal.GetSnapshot().LastFailures);
        Assert.Equal(TestData.Petr4, failure.Ticker);
        Assert.Equal("Yahoo Finance responded with HTTP 503 for PETR4.", failure.Reason);
        Assert.Equal(MonitoringFailureKind.Transient, failure.Kind);
        Assert.Equal(TestData.Now.AddSeconds(2), failure.OccurredAt);
    }

    [Fact]
    public async Task UnknownTicker_IsRecordedAsTickerNotFound()
    {
        _harness.QuoteApi.RespondWithStatus(HttpStatusCode.NotFound);

        await _worker.StartAsync(Ct);
        await Eventually.HoldsAsync(() => StopRequested);
        await _worker.StopAsync(Ct);

        var failure = Assert.Single(_harness.Journal.GetSnapshot().LastFailures);
        Assert.Equal(MonitoringFailureKind.TickerNotFound, failure.Kind);
        Assert.False(failure.IsRecoverable);
    }

    [Fact]
    public async Task Stop_RecordsNoFailure()
    {
        _harness.QuoteApi.RespondSlowly(TimeSpan.FromMinutes(5));

        await _worker.StartAsync(Ct);
        await WaitForCheckAsync();
        await _worker.StopAsync(Ct);

        Assert.Empty(_harness.Journal.GetSnapshot().LastFailures);
    }

    public static TheoryData<Exception, MonitoringFailureKind> FailureKinds => new()
    {
        { new HttpRequestException("network"), MonitoringFailureKind.Transient },
        { new InvalidOperationException("bug"), MonitoringFailureKind.Transient },
        { new QuoteProviderException("HTTP 503"), MonitoringFailureKind.Transient },
        { new TickerNotFoundException(TestData.Petr4), MonitoringFailureKind.TickerNotFound },
        { new QuoteProviderException("brapi.dev rejected the request (HTTP 401).", isRecoverable: false), MonitoringFailureKind.Rejected },
        {
            new QuoteProviderException(
                "All quote providers failed.",
                new AggregateException(new TickerNotFoundException(TestData.Petr4), new QuoteProviderException("HTTP 401", isRecoverable: false)),
                isRecoverable: false),
            MonitoringFailureKind.TickerNotFound
        },
        {
            new QuoteProviderException(
                "All quote providers failed.",
                new AggregateException(new QuoteProviderException("HTTP 401", isRecoverable: false), new QuoteProviderException("HTTP 403", isRecoverable: false)),
                isRecoverable: false),
            MonitoringFailureKind.Rejected
        },
        {
            new QuoteProviderException(
                "All quote providers failed.",
                new AggregateException(new TickerNotFoundException(TestData.Petr4), new QuoteProviderException("HTTP 503")),
                isRecoverable: true),
            MonitoringFailureKind.Transient
        },
    };

    [Theory]
    [MemberData(nameof(FailureKinds))]
    public void Classify_NamesTheCauseOfTheFailure(Exception exception, MonitoringFailureKind expected)
    {
        Assert.Equal(expected, MonitoringWorker.Classify(exception));
    }

    [Theory]
    [InlineData("00:00:05", "5 seconds")]
    [InlineData("00:00:01", "1 second")]
    [InlineData("00:00:30", "30 seconds")]
    [InlineData("00:01:00", "1 minute")]
    [InlineData("00:01:30", "1 minute 30 seconds")]
    [InlineData("01:00:00", "1 hour")]
    [InlineData("02:05:01", "2 hours 5 minutes 1 second")]
    [InlineData("1.00:00:00", "24 hours")]
    public void Describe_WritesTheIntervalInWords(string interval, string expected)
    {
        Assert.Equal(expected, MonitoringWorker.Describe(TimeSpan.Parse(interval, CultureInfo.InvariantCulture)));
    }

    public void Dispose()
    {
        _worker.Dispose();
        _harness.Dispose();
    }

    private static int CheckCompletedCount(MonitoringHarness harness) =>
        harness.WorkerLog.Entries.Count(entry => entry.EventName == MonitoringWorker.CheckCompletedEvent);

    private async Task WaitForCheckAsync() =>
        Assert.True(await _harness.QuoteApi.RequestSignal.WaitAsync(SignalTimeout, Ct), "Expected a quote request.");
}