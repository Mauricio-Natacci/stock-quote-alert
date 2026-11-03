using StockQuoteAlert.Core.Application.Monitoring;

namespace StockQuoteAlert.Cli.CommandLine;

internal abstract record CommandLineResult
{
    private CommandLineResult()
    {
    }

    public sealed record Success(MonitoringTargets Targets) : CommandLineResult;

    public sealed record HelpRequested : CommandLineResult;

    public sealed record Failure(IReadOnlyList<string> Errors) : CommandLineResult;
}