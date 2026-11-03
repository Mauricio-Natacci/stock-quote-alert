using StockQuoteAlert.Cli.CommandLine;
using StockQuoteAlert.Tests.TestDoubles;

namespace StockQuoteAlert.Tests.Cli.CommandLine;

public sealed class CommandLineResultTests
{
    [Fact]
    public void Success_ExposesTargets()
    {
        var success = new CommandLineResult.Success(TestData.PetrAndVale);

        Assert.Same(TestData.PetrAndVale, success.Targets);
    }

    [Fact]
    public void Success_WithSameTargets_AreEqual()
    {
        Assert.Equal(new CommandLineResult.Success(TestData.Targets), new CommandLineResult.Success(TestData.Targets));
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