using StockQuoteAlert.Cli;
using StockQuoteAlert.Cli.CommandLine;
using StockQuoteAlert.Core.Application.Monitoring;

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
        using (var host = StockQuoteAlertApp.Create(new MonitoringTarget(success.Ticker, success.Thresholds)))
        {
            return await StockQuoteAlertApp.RunAsync(host);
        }

    default:
        throw new InvalidOperationException("Unexpected command line result.");
}