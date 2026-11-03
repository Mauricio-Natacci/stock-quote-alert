using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StockQuoteAlert.Core.Application.Abstractions;
using StockQuoteAlert.Core.Application.Alerts;
using StockQuoteAlert.Core.Domain;

namespace StockQuoteAlert.Infrastructure.Messaging;

/// <summary>
/// Delivers each queued alert, in order, to every registered consumer in parallel. Consumers are independent: one
/// failing does not affect the others. A failed delivery is retried after 5 s, 10 s and 20 s, then given up after
/// <see cref="MaxAttempts"/> attempts. The next alert waits until every consumer is done with the current one.
/// </summary>
internal sealed partial class AlertDispatcher(
    InMemoryAlertQueue queue,
    IEnumerable<IAlertConsumer> consumers,
    TimeProvider timeProvider,
    ILogger<AlertDispatcher> logger) : BackgroundService
{
    internal const int MaxAttempts = 4;

    internal static readonly TimeSpan FirstRetryDelay = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var alert in queue.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                await DispatchAsync(alert, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    internal Task DispatchAsync(PriceAlertRaised alert, CancellationToken cancellationToken) =>
        Task.WhenAll(consumers.Select(consumer => DeliverAsync(consumer, alert, cancellationToken)));

    private async Task DeliverAsync(IAlertConsumer consumer, PriceAlertRaised alert, CancellationToken cancellationToken)
    {
        var delay = FirstRetryDelay;

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await consumer.ConsumeAsync(alert, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                if (attempt == MaxAttempts)
                {
                    LogGaveUp(exception, consumer.Name, alert.Alert.Kind, alert.Alert.Ticker.Symbol, attempt);
                    return;
                }

                LogRetrying(consumer.Name, alert.Alert.Kind, alert.Alert.Ticker.Symbol, attempt, MaxAttempts, delay.TotalSeconds, exception.Message);
                await Task.Delay(delay, timeProvider, cancellationToken).ConfigureAwait(false);
                delay *= 2;
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Consumer} failed to handle the {Kind} alert for {Ticker} (attempt {Attempt} of {MaxAttempts}), retrying in {Seconds}s: {Error}")]
    private partial void LogRetrying(string consumer, AlertKind kind, string ticker, int attempt, int maxAttempts, double seconds, string error);

    [LoggerMessage(Level = LogLevel.Error, Message = "{Consumer} gave up on the {Kind} alert for {Ticker} after {Attempts} attempts")]
    private partial void LogGaveUp(Exception exception, string consumer, AlertKind kind, string ticker, int attempts);
}