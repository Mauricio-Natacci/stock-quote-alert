namespace StockQuoteAlert.Cli;

internal static class ExitCodes
{
    public const int Success = 0;
    public const int InvalidArguments = 1;
    public const int InvalidConfiguration = 2;
    public const int UnrecoverableMonitoringFailure = 3;
}