using StockQuoteAlert.Core.Application.Exceptions;
using StockQuoteAlert.Tests.TestDoubles;

namespace StockQuoteAlert.Tests.Core.Application.Exceptions;

public sealed class TickerNotFoundExceptionTests
{
    [Fact]
    public void Constructor_KeepsTickerAndDescribesIt()
    {
        var exception = new TickerNotFoundException(TestData.Petr4);

        Assert.Equal(TestData.Petr4, exception.Ticker);
        Assert.Equal("Ticker 'PETR4' was not found by the quote provider.", exception.Message);
    }

    [Fact]
    public void IsNeverRecoverable()
    {
        Assert.False(new TickerNotFoundException(TestData.Petr4).IsRecoverable);
    }

    [Fact]
    public void IsAQuoteProviderException()
    {
        Assert.IsAssignableFrom<QuoteProviderException>(new TickerNotFoundException(TestData.Petr4));
    }
}