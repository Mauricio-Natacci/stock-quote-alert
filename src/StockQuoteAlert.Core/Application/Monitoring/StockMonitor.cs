using System.Globalization;
using Microsoft.Extensions.Logging;
using StockQuoteAlert.Core.Application.Abstractions;
using StockQuoteAlert.Core.Application.Alerts;
using StockQuoteAlert.Core.Application.Exceptions;
using StockQuoteAlert.Core.Application.Journal;
using StockQuoteAlert.Core.Domain;

namespace StockQuoteAlert.Core.Application.Monitoring;

/// <summary>
/// Watches one stock. Each check fetches the quote, classifies it and publishes an alert only when the price enters a
/// new alert zone, so a price that stays above/below a reference does not produce one alert per check. Delivery (and
/// its retries) is left to the consumers of the published alert, which run in the background. Every quote is recorded
/// in the journal. Checks must not run concurrently.
/// </summary>
public sealed partial class StockMonitor(
    IQuoteProvider quoteProvider,
    IAlertPublisher publisher,
    IMonitoringJournal journal,
    MonitoringTarget target,
    TimeProvider timeProvider,
    ILogger<StockMonitor> logger)
{
    public MonitoringTarget Target => target;

    /// <summary>The zone of the last checked price (<see cref="PriceZone.Neutral"/> before the first check).</summary>
    public PriceZone CurrentZone { get; private set; } = PriceZone.Neutral;

    /// <exception cref="QuoteProviderException">The quote could not be fetched; the zone is left unchanged.</exception>
    public async Task CheckAsync(CancellationToken cancellationToken)
    {
        var quote = await quoteProvider.GetQuoteAsync(target.Ticker, cancellationToken).ConfigureAwait(false);
        var zone = target.Thresholds.Classify(quote.Price);
        var position = Position(zone, target.Thresholds);

        journal.RecordQuote(new QuoteObservation(quote, zone, timeProvider.GetUtcNow()));
        LogQuoteReceived(quote.Ticker.Symbol, quote.Price, quote.Source, position);

        if (zone == CurrentZone)
        {
            return;
        }

        if (PriceAlert.FromZone(quote, zone, target.Thresholds) is { } alert)
        {
            await publisher.PublishAsync(new PriceAlertRaised(Guid.CreateVersion7(), alert, timeProvider.GetUtcNow()), cancellationToken).ConfigureAwait(false);
            LogAlertRaised(alert.Kind, alert.Ticker.Symbol, alert.CurrentPrice);
        }
        else
        {
            LogBackToNeutral(quote.Ticker.Symbol, quote.Price);
        }

        CurrentZone = zone;
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

    [LoggerMessage(Level = LogLevel.Information, Message = "{Kind} alert raised for {Ticker} at {Price:F2}")]
    private partial void LogAlertRaised(AlertKind kind, string ticker, decimal price);
}