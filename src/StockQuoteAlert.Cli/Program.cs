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
        try
        {
            using var host = StockQuoteAlertApp.Create(new MonitoringTarget(success.Ticker, success.Thresholds));

            return await StockQuoteAlertApp.RunAsync(host);
        }
        catch (Exception exception) when (ConfigurationErrors.TryExtract(exception, out var configurationErrors))
        {
            Console.Error.WriteLine("error: invalid configuration");

            foreach (var configurationError in configurationErrors)
            {
                Console.Error.WriteLine($"  - {configurationError}");
            }

            Console.Error.WriteLine();
            Console.Error.WriteLine("Set the values in appsettings.json, user-secrets or environment variables (e.g. Smtp__Password).");
            return ExitCodes.InvalidConfiguration;
        }

    default:
        throw new InvalidOperationException("Unexpected command line result.");
}