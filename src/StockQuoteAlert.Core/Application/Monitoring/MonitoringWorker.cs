using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StockQuoteAlert.Core.Application.Exceptions;

namespace StockQuoteAlert.Core.Application.Monitoring;

/// <summary>
/// Checks the monitored asset as soon as the host starts and then once per <see cref="MonitoringOptions.PollingInterval"/>.
/// A transient failure is logged and retried on the next tick; an unrecoverable failure (unknown ticker, rejected
/// request) stops monitoring, is recorded in <see cref="MonitoringOutcome"/> and stops the application instead of
/// repeating the same error forever.
/// </summary>
public sealed partial class MonitoringWorker(
    StockMonitor monitor,
    IHostApplicationLifetime lifetime,
    MonitoringOutcome outcome,
    IOptions<MonitoringOptions> options,
    TimeProvider timeProvider,
    ILogger<MonitoringWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = options.Value.PollingInterval;
        var target = monitor.Target;

        var every = Describe(interval);

        LogStarted(target.Ticker.Symbol, target.Thresholds.SellAbove, target.Thresholds.BuyBelow, every);

        // Created before the first check so a tick that elapses during it is not lost.
        using var timer = new PeriodicTimer(interval, timeProvider);

        try
        {
            do
            {
                if (!await TryCheckAsync(stoppingToken).ConfigureAwait(false))
                {
                    outcome.MarkUnrecoverableFailure();
                    lifetime.StopApplication();
                    return;
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            LogStopped(target.Ticker.Symbol);
        }
    }

    /// <returns><see langword="false"/> when monitoring cannot go on.</returns>
    private async Task<bool> TryCheckAsync(CancellationToken stoppingToken)
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

    [LoggerMessage(Level = LogLevel.Information, Message = "Stopped monitoring {Ticker}")]
    private partial void LogStopped(string ticker);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to check {Ticker}; retrying on next tick")]
    private partial void LogCheckFailed(Exception exception, string ticker);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Unrecoverable failure while monitoring {Ticker}; shutting down")]
    private partial void LogUnrecoverableFailure(Exception exception, string ticker);
}