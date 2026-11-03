using StockQuoteAlert.Core.Domain;
using StockQuoteAlert.Tests.TestDoubles;

namespace StockQuoteAlert.Tests.Core.Domain;

public sealed class QuoteTests
{
    [Fact]
    public void Constructor_ExposesValues()
    {
        var quote = new Quote(TestData.Petr4, 22.63m, TestData.Now, "Yahoo Finance");

        Assert.Equal(TestData.Petr4, quote.Ticker);
        Assert.Equal(22.63m, quote.Price);
        Assert.Equal(TestData.Now, quote.QuotedAt);
        Assert.Equal("Yahoo Finance", quote.Source);
    }

    [Fact]
    public void Quotes_WithSameValues_AreEqual()
    {
        var first = new Quote(Ticker.Create("petr4"), 22.63m, TestData.Now, "Yahoo Finance");
        var second = new Quote(Ticker.Create("PETR4"), 22.63m, TestData.Now, "Yahoo Finance");

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void Quotes_WithDifferentPrice_AreNotEqual()
    {
        Assert.NotEqual(TestData.QuoteAt(22.63m), TestData.QuoteAt(22.64m));
    }

    [Fact]
    public void Quotes_WithDifferentTicker_AreNotEqual()
    {
        var quote = TestData.QuoteAt(22.63m);

        Assert.NotEqual(quote, quote with { Ticker = Ticker.Create("VALE3") });
    }

    [Fact]
    public void Quotes_WithDifferentTime_AreNotEqual()
    {
        var quote = TestData.QuoteAt(22.63m);

        Assert.NotEqual(quote, quote with { QuotedAt = TestData.Now.AddSeconds(30) });
    }

    [Fact]
    public void Quotes_WithDifferentSource_AreNotEqual()
    {
        var quote = TestData.QuoteAt(22.63m);

        Assert.NotEqual(quote, quote with { Source = "brapi" });
    }
}