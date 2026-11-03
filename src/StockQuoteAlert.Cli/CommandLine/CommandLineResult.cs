using StockQuoteAlert.Core.Domain;

namespace StockQuoteAlert.Cli.CommandLine;

internal abstract record CommandLineResult
{
    private CommandLineResult()
    {
    }

    public sealed record Success(Ticker Ticker, AlertThresholds Thresholds) : CommandLineResult;

    public sealed record HelpRequested : CommandLineResult;

    public sealed record Failure(IReadOnlyList<string> Errors) : CommandLineResult;
}