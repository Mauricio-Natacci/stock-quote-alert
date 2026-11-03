using System.Globalization;
using Microsoft.Extensions.Logging;
using StockQuoteAlert.Core.Application.Abstractions;
using StockQuoteAlert.Core.Application.Exceptions;
using StockQuoteAlert.Core.Application.Journal;
using StockQuoteAlert.Core.Domain;

namespace StockQuoteAlert.Core.Application.Monitoring;

/// <summary>
/// Watches one asset. Each check fetches the quote, classifies it and notifies only when the price enters a new alert
/// zone, so a price that stays above/below a reference does not produce one e-mail per check. When delivery fails the
/// previous zone is kept, so the next check tries again. Every quote is recorded in the journal. Checks must not run
/// concurrently.
/// </summary>
public sealed partial class StockMonitor(
    IQuoteProvider quoteProvider,
    IAlertNotifier notifier,
    IMonitoringJournal journal,
    MonitoringTarget target,
    TimeProvider timeProvider,
    ILogger<StockMonitor> logger)
{
    public MonitoringTarget Target => target;

    /// <summary>The zone the user was last told about (or <see cref="PriceZone.Neutral"/> before any alert).</summary>
    public PriceZone CurrentZone { get; private set; } = PriceZone.Neutral;

    /// <exception cref="QuoteProviderException">The quote could not be fetched; the zone is left unchanged.</exception>
    public async Task CheckAsync(CancellationToken cancellationToken)
    {
        var quote = await quoteProvider.GetQuoteAsync(target.Ticker, cancellationToken).ConfigureAwait(false);
        var zone = target.Thresholds.Classify(quote.Price);

        journal.RecordQuote(new QuoteObservation(quote, zone, timeProvider.GetUtcNow()));
        var position = Position(zone, target.Thresholds);

        LogQuoteReceived(quote.Ticker.Symbol, quote.Price, quote.Source, position);

        if (zone == CurrentZone)
        {
            return;
        }

        if (PriceAlert.FromZone(quote, zone, target.Thresholds) is not { } alert)
        {
            LogBackToNeutral(quote.Ticker.Symbol, quote.Price);
            CurrentZone = zone;
            return;
        }

        if (await notifier.NotifyAsync(alert, cancellationToken).ConfigureAwait(false))
        {
            LogAlertSent(alert.Kind, alert.Ticker.Symbol, alert.CurrentPrice);
            CurrentZone = zone;
        }
        else
        {
            LogAlertNotDelivered(alert.Kind, alert.Ticker.Symbol, alert.CurrentPrice);
        }
    }

    /// <summary>Where the price stands, for the log: "above sell 22.67", "below buy 22.59" or "neutral".</summary>
    internal static string Position(PriceZone zone, AlertThresholds thresholds) => zone switch
    {
        PriceZone.AboveSellThreshold => string.Create(CultureInfo.InvariantCulture, $"above sell {thresholds.SellAbove:F2}"),
        PriceZone.BelowBuyThreshold => string.Create(CultureInfo.InvariantCulture, $"below buy {thresholds.BuyBelow:F2}"),
        _ => "neutral",
    };

    [LoggerMessage(Level = LogLevel.Information, Message = "{Ticker} quoted at {Price:F2} by {Source} ({Position})")]
    private partial void LogQuoteReceived(string ticker, decimal price, string source, string position);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Ticker} is back between thresholds at {Price:F2}")]
    private partial void LogBackToNeutral(string ticker, decimal price);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Kind} alert sent for {Ticker} at {Price:F2}")]
    private partial void LogAlertSent(AlertKind kind, string ticker, decimal price);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Kind} alert for {Ticker} at {Price:F2} was not delivered; it will be retried on the next check")]
    private partial void LogAlertNotDelivered(AlertKind kind, string ticker, decimal price);
}