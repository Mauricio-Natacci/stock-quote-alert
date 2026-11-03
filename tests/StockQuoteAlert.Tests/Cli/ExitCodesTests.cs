using StockQuoteAlert.Cli;

namespace StockQuoteAlert.Tests.Cli;

public sealed class ExitCodesTests
{
    [Fact]
    public void ExitCodes_HaveDocumentedValues()
    {
        Assert.Equal(0, ExitCodes.Success);
        Assert.Equal(1, ExitCodes.InvalidArguments);
    }
}