using StockQuoteAlert.Core.Domain;

namespace StockQuoteAlert.Tests.Core.Domain;

public sealed class AlertThresholdsTests
{
    private readonly AlertThresholds _thresholds = new(sellAbove: 22.67m, buyBelow: 22.59m);

    public static TheoryData<decimal, PriceZone> PricesAndZones => new()
    {
        { 30m, PriceZone.AboveSellThreshold },
        { 22.68m, PriceZone.AboveSellThreshold },
        { 22.671m, PriceZone.AboveSellThreshold },
        { 22.67m, PriceZone.Neutral },
        { 22.63m, PriceZone.Neutral },
        { 22.59m, PriceZone.Neutral },
        { 22.589m, PriceZone.BelowBuyThreshold },
        { 22.58m, PriceZone.BelowBuyThreshold },
        { 1m, PriceZone.BelowBuyThreshold },
    };

    [Theory]
    [MemberData(nameof(PricesAndZones))]
    public void Classify_ReturnsZoneForPrice(decimal price, PriceZone expected)
    {
        Assert.Equal(expected, _thresholds.Classify(price));
    }

    [Theory]
    [InlineData(0, 10, "sellAbove")]
    [InlineData(-1, 10, "sellAbove")]
    [InlineData(10, 0, "buyBelow")]
    [InlineData(10, -1, "buyBelow")]
    public void Constructor_WithNonPositivePrice_Throws(int sellAbove, int buyBelow, string expectedParam)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new AlertThresholds(sellAbove, buyBelow));

        Assert.Equal(expectedParam, exception.ParamName);
    }

    [Theory]
    [InlineData(10, 10)]
    [InlineData(10, 11)]
    public void Constructor_WhenSellIsNotAboveBuy_Throws(int sellAbove, int buyBelow)
    {
        var exception = Assert.Throws<ArgumentException>(() => new AlertThresholds(sellAbove, buyBelow));

        Assert.Equal("sellAbove", exception.ParamName);
    }

    [Fact]
    public void Constructor_ExposesThresholds()
    {
        Assert.Equal(22.67m, _thresholds.SellAbove);
        Assert.Equal(22.59m, _thresholds.BuyBelow);
    }

    [Fact]
    public void Thresholds_WithSameValues_AreEqual()
    {
        Assert.Equal(new AlertThresholds(22.67m, 22.59m), _thresholds);
    }
}