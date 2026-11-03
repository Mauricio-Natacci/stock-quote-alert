using StockQuoteAlert.Core.Domain;
using StockQuoteAlert.Tests.TestDoubles;

namespace StockQuoteAlert.Tests.Core.Domain;

public sealed class PriceAlertTests
{
    [Fact]
    public void FromZone_AboveSell_CreatesSellAlertWithSellReference()
    {
        var quote = TestData.QuoteAt(22.70m);

        var alert = PriceAlert.FromZone(quote, PriceZone.AboveSellThreshold, TestData.Thresholds);

        Assert.Equal(new PriceAlert(TestData.Petr4, AlertKind.Sell, 22.70m, 22.67m, TestData.Now), alert);
    }

    [Fact]
    public void FromZone_BelowBuy_CreatesBuyAlertWithBuyReference()
    {
        var quote = TestData.QuoteAt(22.50m);

        var alert = PriceAlert.FromZone(quote, PriceZone.BelowBuyThreshold, TestData.Thresholds);

        Assert.Equal(new PriceAlert(TestData.Petr4, AlertKind.Buy, 22.50m, 22.59m, TestData.Now), alert);
    }

    [Fact]
    public void FromZone_Neutral_ReturnsNull()
    {
        Assert.Null(PriceAlert.FromZone(TestData.QuoteAt(22.63m), PriceZone.Neutral, TestData.Thresholds));
    }

    [Fact]
    public void FromZone_UnknownZone_Throws()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => PriceAlert.FromZone(TestData.QuoteAt(22.63m), (PriceZone)42, TestData.Thresholds));

        Assert.Equal("zone", exception.ParamName);
    }
}