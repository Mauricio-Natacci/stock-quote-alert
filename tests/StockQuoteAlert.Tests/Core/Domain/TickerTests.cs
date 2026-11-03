using StockQuoteAlert.Core.Domain;

namespace StockQuoteAlert.Tests.Core.Domain;

public sealed class TickerTests
{
    [Theory]
    [InlineData("PETR4", "PETR4")]
    [InlineData("vale3", "VALE3")]
    [InlineData("  BOVA11 ", "BOVA11")]
    [InlineData("PETR4F", "PETR4F")]
    [InlineData("B3SA3", "B3SA3")]
    [InlineData("m1ta34", "M1TA34")]
    public void TryCreate_WithValidSymbol_ReturnsNormalizedTicker(string input, string expected)
    {
        var created = Ticker.TryCreate(input, out var ticker);

        Assert.True(created);
        Assert.Equal(expected, ticker!.Symbol);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("PETR")]
    [InlineData("PETR123")]
    [InlineData("PET4")]
    [InlineData("1234")]
    [InlineData("3BSA3")]
    [InlineData("PETR4G")]
    [InlineData("PE TR4")]
    [InlineData("PETR4.SA")]
    public void TryCreate_WithInvalidSymbol_ReturnsFalse(string? input)
    {
        var created = Ticker.TryCreate(input, out var ticker);

        Assert.False(created);
        Assert.Null(ticker);
    }

    [Fact]
    public void Create_WithValidSymbol_ReturnsNormalizedTicker()
    {
        Assert.Equal("PETR4", Ticker.Create(" petr4 ").Symbol);
    }

    [Fact]
    public void Create_WithInvalidSymbol_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() => Ticker.Create("INVALID"));

        Assert.Contains("INVALID", exception.Message, StringComparison.Ordinal);
        Assert.Equal("value", exception.ParamName);
    }

    [Fact]
    public void Tickers_WithSameSymbol_AreEqual()
    {
        Assert.Equal(Ticker.Create("petr4"), Ticker.Create("PETR4"));
    }

    [Fact]
    public void ToString_ReturnsSymbol()
    {
        Assert.Equal("PETR4", Ticker.Create("petr4").ToString());
    }
}