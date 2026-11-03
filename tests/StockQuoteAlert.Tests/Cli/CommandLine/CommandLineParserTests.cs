using StockQuoteAlert.Cli.CommandLine;
using StockQuoteAlert.Core.Domain;

namespace StockQuoteAlert.Tests.Cli.CommandLine;

public sealed class CommandLineParserTests
{
    [Fact]
    public void Parse_ValidArguments_ReturnsTickerAndThresholds()
    {
        var result = CommandLineParser.Parse(["PETR4", "22.67", "22.59"]);

        Assert.Equal(new CommandLineResult.Success(Ticker.Create("PETR4"), new AlertThresholds(22.67m, 22.59m)), result);
    }

    [Fact]
    public void Parse_LowerCaseTicker_IsNormalized()
    {
        var success = Assert.IsType<CommandLineResult.Success>(CommandLineParser.Parse(["petr4", "22.67", "22.59"]));

        Assert.Equal("PETR4", success.Ticker.Symbol);
    }

    [Theory]
    [InlineData("22,67", "22,59")]
    [InlineData("22.67", "22,59")]
    [InlineData("22,67", "22.59")]
    public void Parse_AcceptsDotOrCommaAsDecimalSeparator(string sell, string buy)
    {
        var success = Assert.IsType<CommandLineResult.Success>(CommandLineParser.Parse(["PETR4", sell, buy]));

        Assert.Equal(22.67m, success.Thresholds.SellAbove);
        Assert.Equal(22.59m, success.Thresholds.BuyBelow);
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
        Assert.IsType<CommandLineResult.HelpRequested>(CommandLineParser.Parse(["PETR4", "--help", "22.59"]));
    }

    [Theory]
    [InlineData("0 arguments")]
    [InlineData("1 argument", "PETR4")]
    [InlineData("2 arguments", "PETR4", "22.67")]
    [InlineData("4 arguments", "PETR4", "22.67", "22.59", "VALE3")]
    [InlineData("6 arguments", "PETR4", "22.67", "22.59", "VALE3", "61.50", "60.80")]
    public void Parse_WrongArgumentCount_Fails(string received, params string[] args)
    {
        var failure = Assert.IsType<CommandLineResult.Failure>(CommandLineParser.Parse(args));

        Assert.Equal($"Expected 3 arguments (TICKER SELL_PRICE BUY_PRICE) but received {received}.", Assert.Single(failure.Errors));
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
    public void Parse_InvalidArgument_FailsWithDescriptiveError(string ticker, string sell, string buy, string expectedError)
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
    public void Parse_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => CommandLineParser.Parse(null!));
    }

    [Fact]
    public void Usage_DescribesArgumentsAndExample()
    {
        Assert.StartsWith("Usage: stock-quote-alert <TICKER> <SELL_PRICE> <BUY_PRICE>", CommandLineParser.Usage, StringComparison.Ordinal);
        Assert.Contains("stock-quote-alert PETR4 22.67 22.59", CommandLineParser.Usage, StringComparison.Ordinal);
    }
}