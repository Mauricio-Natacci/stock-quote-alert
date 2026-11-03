using System.Globalization;
using System.Net;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using StockQuoteAlert.Core.Application.Abstractions;
using StockQuoteAlert.Core.Application.Exceptions;
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
    public async Task PriceCrossingAReference_SendsTheAlertOnce()
    {
        _harness.QuoteApi.RespondWithPrices(22.63m, 22.70m, 22.75m);

        await _worker.StartAsync(Ct);
        await WaitForCheckAsync();
        _harness.Time.Advance(Interval);
        await WaitForCheckAsync();
        _harness.Time.Advance(Interval);
        await WaitForCheckAsync();
        await _worker.StopAsync(Ct);

        Assert.StartsWith("[VENDA] PETR4 subiu para R$ 22,70", Assert.Single(_harness.Smtp.Delivered).Subject, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FailedDelivery_IsRetriedOnTheNextTick()
    {
        _harness.QuoteApi.RespondWithPrices(22.70m);
        _harness.Smtp.ConnectionFailure = new IOException("Connection refused");

        await _worker.StartAsync(Ct);
        await Eventually.HoldsAsync(() => _harness.Smtp.Clients.Count == 1);
        _harness.Smtp.ConnectionFailure = null;
        _harness.Time.Advance(Interval);
        await Eventually.HoldsAsync(() => _harness.Smtp.Delivered.Count == 1);
        await _worker.StopAsync(Ct);

        Assert.Equal(PriceZone.AboveSellThreshold, _harness.Monitor.CurrentZone);
    }

    [Fact]
    public async Task RecoverableFailure_IsLoggedAndMonitoringContinues()
    {
        _harness.QuoteApi.RespondWithStatus(HttpStatusCode.ServiceUnavailable).RespondWithPrices(22.70m);

        await _worker.StartAsync(Ct);
        await WaitForCheckAsync();
        await Eventually.HoldsAsync(() => _harness.WorkerLog.Entries.Any(entry => entry.Level == LogLevel.Error));
        _harness.Time.Advance(Interval);
        await Eventually.HoldsAsync(() => _harness.Smtp.Delivered.Count == 1);
        await _worker.StopAsync(Ct);

        var error = Assert.Single(_harness.WorkerLog.Entries, entry => entry.Level == LogLevel.Error);
        Assert.Equal("Failed to check PETR4; retrying on next tick", error.Message);
        Assert.IsType<QuoteProviderException>(error.Exception);
        Assert.False(StopRequested);
        Assert.False(_harness.Outcome.StoppedByUnrecoverableFailure);
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
        Assert.False(_harness.Outcome.StoppedByUnrecoverableFailure);
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

        var critical = Assert.Single(_harness.WorkerLog.Entries, entry => entry.Level == LogLevel.Critical);
        Assert.Equal("Unrecoverable failure while monitoring PETR4; shutting down", critical.Message);
        Assert.IsType<TickerNotFoundException>(critical.Exception);
        Assert.True(_harness.Outcome.StoppedByUnrecoverableFailure);
    }

    [Fact]
    public async Task RejectedRequest_RecordsUnrecoverableFailureAndStopsTheApplication()
    {
        using var harness = new MonitoringHarness();
        var quoteProvider = Substitute.For<IQuoteProvider>();
        quoteProvider
            .GetQuoteAsync(Arg.Any<Ticker>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<Quote>(new QuoteProviderException("Token rejected (HTTP 401).", isRecoverable: false)));
        var monitor = new StockMonitor(quoteProvider, Substitute.For<IAlertNotifier>(), TestData.Target, harness.MonitorLog);
        using var worker = new MonitoringWorker(
            monitor,
            _lifetime,
            harness.Outcome,
            Options.Create(new MonitoringOptions { PollingInterval = Interval }),
            harness.Time,
            harness.WorkerLog);

        await worker.StartAsync(Ct);
        await Eventually.HoldsAsync(() => StopRequested);
        await worker.StopAsync(Ct);

        Assert.Single(harness.WorkerLog.Entries, entry => entry.Level == LogLevel.Critical);
        Assert.True(harness.Outcome.StoppedByUnrecoverableFailure);
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
    public async Task Stop_CompletesGracefullyAndLogs()
    {
        await _worker.StartAsync(Ct);
        await WaitForCheckAsync();

        await _worker.StopAsync(Ct);

        Assert.True(_worker.ExecuteTask!.IsCompletedSuccessfully);
        Assert.Equal("Stopped monitoring PETR4", _harness.WorkerLog.Entries.Last().Message);
        Assert.False(StopRequested);
        Assert.False(_harness.Outcome.StoppedByUnrecoverableFailure);
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

    private async Task WaitForCheckAsync() =>
        Assert.True(await _harness.QuoteApi.RequestSignal.WaitAsync(SignalTimeout, Ct), "Expected a quote request.");
}