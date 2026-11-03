using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StockQuoteAlert.Core.Application.Exceptions;

namespace StockQuoteAlert.Core.Application.Monitoring;

/// <summary>
/// Checks every monitored stock as soon as the host starts and then once per
/// <see cref="MonitoringOptions.PollingInterval"/>. The stocks are checked in parallel, so a slow response for one
/// does not delay the others. A transient failure is logged and retried on the next tick; a stock with an
/// unrecoverable failure (unknown ticker, rejected request) stops being checked while the others carry on. When no
/// stock is left, that is recorded in <see cref="MonitoringOutcome"/> and the application stops.
/// </summary>
public sealed partial class MonitoringWorker(
    IEnumerable<StockMonitor> monitors,
    IHostApplicationLifetime lifetime,
    MonitoringOutcome outcome,
    IOptions<MonitoringOptions> options,
    TimeProvider timeProvider,
    ILogger<MonitoringWorker> logger) : BackgroundService
{
    /// <summary>Event name of the entry that closes a check of several stocks; the console draws it as a separator.</summary>
    public const string CheckCompletedEvent = "CheckCompleted";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = options.Value.PollingInterval;
        var every = Describe(interval);
        List<StockMonitor> active = [.. monitors];

        foreach (var monitor in active)
        {
            LogStarted(monitor.Target.Ticker.Symbol, monitor.Target.Thresholds.SellAbove, monitor.Target.Thresholds.BuyBelow, every);
        }

        // Created before the first check so a tick that elapses during it is not lost.
        using var timer = new PeriodicTimer(interval, timeProvider);

        try
        {
            do
            {
                var keepChecking = await Task.WhenAll(active.Select(monitor => TryCheckAsync(monitor, stoppingToken))).ConfigureAwait(false);

                // Groups the quotes of one check in the console; with a single stock every line is its own check.
                if (active.Count > 1)
                {
                    LogCheckCompleted();
                }

                active = [.. active.Where((_, index) => keepChecking[index])];

                if (active.Count == 0)
                {
                    LogNothingLeftToMonitor();
                    outcome.MarkNothingLeftToMonitor();
                    lifetime.StopApplication();
                    return;
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            foreach (var monitor in active)
            {
                LogStopped(monitor.Target.Ticker.Symbol);
            }
        }
    }

    /// <returns><see langword="false"/> when this stock cannot be monitored anymore.</returns>
    private async Task<bool> TryCheckAsync(StockMonitor monitor, CancellationToken stoppingToken)
    {
        var ticker = monitor.Target.Ticker.Symbol;

        try
        {
            await monitor.CheckAsync(stoppingToken).ConfigureAwait(false);
            return true;
        }
        catch (QuoteProviderException exception) when (!exception.IsRecoverable)
        {
            LogUnrecoverableFailure(exception, ticker);
            return false;
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
        {
            LogCheckFailed(exception, ticker);
            return true;
        }
    }

    /// <summary>Human-readable interval, e.g. "30 seconds" or "1 minute 30 seconds".</summary>
    internal static string Describe(TimeSpan interval)
    {
        var parts = new (int Value, string Unit)[]
        {
            ((int)interval.TotalHours, "hour"),
            (interval.Minutes, "minute"),
            (interval.Seconds, "second"),
        };

        return string.Join(' ', parts.Where(part => part.Value > 0).Select(part => $"{part.Value} {part.Unit}{(part.Value == 1 ? "" : "s")}"));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Monitoring {Ticker}: sell above {SellAbove:F2}, buy below {BuyBelow:F2}, every {Interval}")]
    private partial void LogStarted(string ticker, decimal sellAbove, decimal buyBelow, string interval);

    [LoggerMessage(Level = LogLevel.Information, EventName = CheckCompletedEvent, Message = "Check completed")]
    private partial void LogCheckCompleted();

    [LoggerMessage(Level = LogLevel.Information, Message = "Stopped monitoring {Ticker}")]
    private partial void LogStopped(string ticker);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to check {Ticker}; retrying on next tick")]
    private partial void LogCheckFailed(Exception exception, string ticker);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Unrecoverable failure while monitoring {Ticker}; it will no longer be checked")]
    private partial void LogUnrecoverableFailure(Exception exception, string ticker);

    [LoggerMessage(Level = LogLevel.Critical, Message = "No stock left to monitor; shutting down")]
    private partial void LogNothingLeftToMonitor();
}