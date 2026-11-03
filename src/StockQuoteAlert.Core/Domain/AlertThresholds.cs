namespace StockQuoteAlert.Core.Domain;

/// <summary>
/// The sell and buy references for a stock. Both are exclusive: a price equal to a reference is neutral.
/// </summary>
public sealed record AlertThresholds
{
    public AlertThresholds(decimal sellAbove, decimal buyBelow)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sellAbove);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(buyBelow);

        if (sellAbove <= buyBelow)
        {
            throw new ArgumentException(
                $"Sell reference ({sellAbove}) must be greater than buy reference ({buyBelow}).",
                nameof(sellAbove));
        }

        SellAbove = sellAbove;
        BuyBelow = buyBelow;
    }

    public decimal SellAbove { get; }

    public decimal BuyBelow { get; }

    public PriceZone Classify(decimal price)
    {
        if (price > SellAbove)
        {
            return PriceZone.AboveSellThreshold;
        }

        if (price < BuyBelow)
        {
            return PriceZone.BelowBuyThreshold;
        }

        return PriceZone.Neutral;
    }
}