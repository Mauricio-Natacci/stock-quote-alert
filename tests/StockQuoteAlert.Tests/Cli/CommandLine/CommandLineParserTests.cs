using StockQuoteAlert.Cli.CommandLine;
using StockQuoteAlert.Core.Application.Monitoring;
using StockQuoteAlert.Core.Domain;

namespace StockQuoteAlert.Tests.Cli.CommandLine;

public sealed class CommandLineParserTests
{
    [Fact]
    public void Parse_OneStock_ReturnsItsTarget()
    {
        var success = Assert.IsType<CommandLineResult.Success>(CommandLineParser.Parse(["PETR4", "22.67", "22.59"]));

        Assert.Equal([Target("PETR4", 22.67m, 22.59m)], success.Targets);
    }

    [Fact]
    public void Parse_RepeatedTrios_ReturnOneTargetPerStockInOrder()
    {
        var success = Assert.IsType<CommandLineResult.Success>(
            CommandLineParser.Parse(["PETR4", "22.67", "22.59", "vale3", "61,50", "60.80", "BOVA11", "130", "125"]));

        Assert.Equal(
            [Target("PETR4", 22.67m, 22.59m), Target("VALE3", 61.50m, 60.80m), Target("BOVA11", 130m, 125m)],
            success.Targets);
    }

    [Fact]
    public void Parse_LowerCaseTicker_IsNormalized()
    {
        var success = Assert.IsType<CommandLineResult.Success>(CommandLineParser.Parse(["petr4", "22.67", "22.59"]));

        Assert.Equal("PETR4", Assert.Single(success.Targets).Ticker.Symbol);
    }

    [Theory]
    [InlineData("22,67", "22,59")]
    [InlineData("22.67", "22,59")]
    [InlineData("22,67", "22.59")]
    public void Parse_AcceptsDotOrCommaAsDecimalSeparator(string sell, string buy)
    {
        var success = Assert.IsType<CommandLineResult.Success>(CommandLineParser.Parse(["PETR4", sell, buy]));

        var target = Assert.Single(success.Targets);
        Assert.Equal(22.67m, target.Thresholds.SellAbove);
        Assert.Equal(22.59m, target.Thresholds.BuyBelow);
    }

    [Theory]
    [InlineData("23", "22")]
    [InlineData("23.", "22")]
    [InlineData(".5", ".4")]
    public void Parse_AcceptsIntegerAndPartialDecimals(string sell, string buy)
    {
        Assert.IsType<CommandLineResult.Success>(CommandLineParser.Parse(["PETR4", sell, buy]));
    }

    [Theory]
    [InlineData("-h")]
    [InlineData("--help")]
    [InlineData("--HELP")]
    [InlineData("-?")]
    [InlineData("/?")]
    public void Parse_HelpFlag_ReturnsHelpRequested(string flag)
    {
        Assert.IsType<CommandLineResult.HelpRequested>(CommandLineParser.Parse([flag]));
    }

    [Fact]
    public void Parse_HelpFlagAmongOtherArguments_ReturnsHelpRequested()
    {
        Assert.IsType<CommandLineResult.HelpRequested>(CommandLineParser.Parse(["PETR4", "22.67", "22.59", "VALE3", "--help"]));
    }

    [Theory]
    [InlineData("0 arguments")]
    [InlineData("1 argument", "PETR4")]
    [InlineData("2 arguments", "PETR4", "22.67")]
    [InlineData("4 arguments", "PETR4", "22.67", "22.59", "VALE3")]
    [InlineData("5 arguments", "PETR4", "22.67", "22.59", "VALE3", "61.50")]
    [InlineData("7 arguments", "PETR4", "22.67", "22.59", "VALE3", "61.50", "60.80", "ITUB4")]
    public void Parse_ArgumentCountNotMultipleOfThree_Fails(string received, params string[] args)
    {
        var failure = Assert.IsType<CommandLineResult.Failure>(CommandLineParser.Parse(args));

        Assert.Equal($"Expected TICKER SELL_PRICE BUY_PRICE (3 arguments per stock) but received {received}.", Assert.Single(failure.Errors));
    }

    [Theory]
    [InlineData("PETR", "22.67", "22.59", "Invalid ticker 'PETR'. Expected a B3 ticker such as PETR4 or BOVA11.")]
    [InlineData("PETR4", "abc", "22.59", "Invalid sell price 'abc'. Expected a positive number such as 22.67.")]
    [InlineData("PETR4", "22.67", "abc", "Invalid buy price 'abc'. Expected a positive number such as 22.59.")]
    [InlineData("PETR4", "0", "22.59", "Invalid sell price '0'. Expected a positive number such as 22.67.")]
    [InlineData("PETR4", "22.67", "-1", "Invalid buy price '-1'. Expected a positive number such as 22.59.")]
    [InlineData("PETR4", "1.234,56", "22.59", "Invalid sell price '1.234,56'. Expected a positive number such as 22.67.")]
    [InlineData("PETR4", "22.67.1", "22.59", "Invalid sell price '22.67.1'. Expected a positive number such as 22.67.")]
    [InlineData("PETR4", "1e3", "22.59", "Invalid sell price '1e3'. Expected a positive number such as 22.67.")]
    [InlineData("PETR4", " 22.67", "22.59", "Invalid sell price ' 22.67'. Expected a positive number such as 22.67.")]
    [InlineData("PETR4", "", "22.59", "Invalid sell price ''. Expected a positive number such as 22.67.")]
    [InlineData("PETR4", "99999999999999999999999999999", "22.59", "Invalid sell price '99999999999999999999999999999'. Expected a positive number such as 22.67.")]
    public void Parse_OneStockWithInvalidArgument_FailsWithoutStockPrefix(string ticker, string sell, string buy, string expectedError)
    {
        var failure = Assert.IsType<CommandLineResult.Failure>(CommandLineParser.Parse([ticker, sell, buy]));

        Assert.Equal(expectedError, Assert.Single(failure.Errors));
    }

    [Theory]
    [InlineData("22.59", "22.67", "Sell price (22.59) must be greater than buy price (22.67).")]
    [InlineData("22.67", "22.67", "Sell price (22.67) must be greater than buy price (22.67).")]
    [InlineData("22,67", "22.67", "Sell price (22.67) must be greater than buy price (22.67).")]
    public void Parse_SellNotAboveBuy_Fails(string sell, string buy, string expectedError)
    {
        var failure = Assert.IsType<CommandLineResult.Failure>(CommandLineParser.Parse(["PETR4", sell, buy]));

        Assert.Equal(expectedError, Assert.Single(failure.Errors));
    }

    [Fact]
    public void Parse_SeveralInvalidArguments_ReportsAllErrorsInOrder()
    {
        var failure = Assert.IsType<CommandLineResult.Failure>(CommandLineParser.Parse(["X", "a", "b"]));

        Assert.Collection(
            failure.Errors,
            error => Assert.StartsWith("Invalid ticker 'X'", error, StringComparison.Ordinal),
            error => Assert.StartsWith("Invalid sell price 'a'", error, StringComparison.Ordinal),
            error => Assert.StartsWith("Invalid buy price 'b'", error, StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_SeveralStocks_ReportsErrorsOfEveryStockSayingWhichOneItIs()
    {
        var failure = Assert.IsType<CommandLineResult.Failure>(
            CommandLineParser.Parse(["PETR4", "22.67", "22.59", "VAL3", "abc", "60.80", "ITUB4", "30", "31"]));

        Assert.Equal(
            [
                "Stock 2 (VAL3 abc 60.80): Invalid ticker 'VAL3'. Expected a B3 ticker such as PETR4 or BOVA11.",
                "Stock 2 (VAL3 abc 60.80): Invalid sell price 'abc'. Expected a positive number such as 22.67.",
                "Stock 3 (ITUB4 30 31): Sell price (30) must be greater than buy price (31).",
            ],
            failure.Errors);
    }

    [Fact]
    public void Parse_SameTickerTwice_Fails()
    {
        var failure = Assert.IsType<CommandLineResult.Failure>(CommandLineParser.Parse(["PETR4", "22.67", "22.59", "petr4", "30", "20"]));

        Assert.Equal("PETR4 was given more than once. Each stock must appear a single time.", Assert.Single(failure.Errors));
    }

    [Fact]
    public void Parse_RepeatedTickersAndInvalidStock_ReportsBoth()
    {
        var failure = Assert.IsType<CommandLineResult.Failure>(
            CommandLineParser.Parse(["PETR4", "22.67", "22.59", "PETR4", "30", "20", "VALE3", "1", "2", "VALE3", "61.50", "60.80", "VALE3", "62", "61"]));

        Assert.Equal(
            [
                "Stock 3 (VALE3 1 2): Sell price (1) must be greater than buy price (2).",
                "PETR4 was given more than once. Each stock must appear a single time.",
                "VALE3 was given more than once. Each stock must appear a single time.",
            ],
            failure.Errors);
    }

    [Fact]
    public void Parse_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => CommandLineParser.Parse(null!));
    }

    [Fact]
    public void Usage_DescribesArgumentsAndBothExamples()
    {
        var usage = CommandLineParser.Usage.ReplaceLineEndings("\n");

        Assert.StartsWith("Usage: stock-quote-alert <TICKER> <SELL_PRICE> <BUY_PRICE> [<TICKER> <SELL_PRICE> <BUY_PRICE> ...]", usage, StringComparison.Ordinal);
        Assert.Contains("Repeat the three values to monitor more stocks; each alert is sent as a separate e-mail.", usage, StringComparison.Ordinal);
        Assert.Contains("  stock-quote-alert PETR4 22.67 22.59\n", usage, StringComparison.Ordinal);
        Assert.Contains("  stock-quote-alert PETR4 22.67 22.59 VALE3 61.50 60.80", usage, StringComparison.Ordinal);
    }

    private static MonitoringTarget Target(string ticker, decimal sell, decimal buy) =>
        new(Ticker.Create(ticker), new AlertThresholds(sell, buy));
}