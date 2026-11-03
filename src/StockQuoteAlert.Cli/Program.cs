using StockQuoteAlert.Cli;
using StockQuoteAlert.Cli.CommandLine;

switch (CommandLineParser.Parse(args))
{
    case CommandLineResult.HelpRequested:
        Console.WriteLine(CommandLineParser.Usage);
        return ExitCodes.Success;

    case CommandLineResult.Failure failure:
        foreach (var error in failure.Errors)
        {
            Console.Error.WriteLine($"error: {error}");
        }

        Console.Error.WriteLine();
        Console.Error.WriteLine(CommandLineParser.Usage);
        return ExitCodes.InvalidArguments;

    case CommandLineResult.Success success:
        Console.WriteLine(
            $"Monitoring {success.Ticker} (sell above {success.Thresholds.SellAbove}, buy below {success.Thresholds.BuyBelow}): not implemented yet.");
        return ExitCodes.Success;

    default:
        throw new InvalidOperationException("Unexpected command line result.");
}