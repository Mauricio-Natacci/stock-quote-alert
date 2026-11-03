using StockQuoteAlert.Infrastructure.Quotes;
using StockQuoteAlert.Tests.TestDoubles;

namespace StockQuoteAlert.Tests.Infrastructure.Quotes;

public sealed class RawQuoteTests
{
    [Fact]
    public void Constructor_ExposesValuesAsGiven()
    {
        var raw = new RawQuote(22.6349m, TestData.Now);

        Assert.Equal(22.6349m, raw.Price);
        Assert.Equal(TestData.Now, raw.QuotedAt);
    }

    [Fact]
    public void RawQuotes_WithSameValues_AreEqual()
    {
        Assert.Equal(new RawQuote(22.63m, TestData.Now), new RawQuote(22.63m, TestData.Now));
    }

    [Fact]
    public void RawQuotes_WithDifferentValues_AreNotEqual()
    {
        var raw = new RawQuote(22.63m, TestData.Now);

        Assert.NotEqual(raw, raw with { Price = 22.64m });
        Assert.NotEqual(raw, raw with { QuotedAt = TestData.Now.AddSeconds(1) });
    }
}