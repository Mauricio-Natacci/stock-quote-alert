using StockQuoteAlert.Cli.CommandLine;
using StockQuoteAlert.Tests.TestDoubles;

namespace StockQuoteAlert.Tests.Cli.CommandLine;

public sealed class CommandLineResultTests
{
    [Fact]
    public void Success_ExposesTickerAndThresholds()
    {
        var success = new CommandLineResult.Success(TestData.Petr4, TestData.Thresholds);

        Assert.Equal(TestData.Petr4, success.Ticker);
        Assert.Equal(TestData.Thresholds, success.Thresholds);
    }

    [Fact]
    public void Success_WithSameValues_AreEqual()
    {
        Assert.Equal(
            new CommandLineResult.Success(TestData.Petr4, TestData.Thresholds),
            new CommandLineResult.Success(TestData.Petr4, TestData.Thresholds));
    }

    [Fact]
    public void HelpRequested_AreEqual()
    {
        Assert.Equal(new CommandLineResult.HelpRequested(), new CommandLineResult.HelpRequested());
    }

    [Fact]
    public void Failure_ExposesErrors()
    {
        var failure = new CommandLineResult.Failure(["first", "second"]);

        Assert.Equal(["first", "second"], failure.Errors);
    }
}