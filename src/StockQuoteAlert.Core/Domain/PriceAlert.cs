namespace StockQuoteAlert.Core.Domain;

/// <param name="ReferencePrice">The threshold that was crossed: the sell reference for a sell alert, the buy reference for a buy alert.</param>
public sealed record PriceAlert(Ticker Ticker, AlertKind Kind, decimal CurrentPrice, decimal ReferencePrice, DateTimeOffset QuotedAt)
{
    /// <summary>Builds the alert for a quote in the given zone, or returns <see langword="null"/> for the neutral zone.</summary>
    public static PriceAlert? FromZone(Quote quote, PriceZone zone, AlertThresholds thresholds) => zone switch
    {
        PriceZone.AboveSellThreshold => new PriceAlert(quote.Ticker, AlertKind.Sell, quote.Price, thresholds.SellAbove, quote.QuotedAt),
        PriceZone.BelowBuyThreshold => new PriceAlert(quote.Ticker, AlertKind.Buy, quote.Price, thresholds.BuyBelow, quote.QuotedAt),
        PriceZone.Neutral => null,
        _ => throw new ArgumentOutOfRangeException(nameof(zone), zone, "Unknown price zone."),
    };
}