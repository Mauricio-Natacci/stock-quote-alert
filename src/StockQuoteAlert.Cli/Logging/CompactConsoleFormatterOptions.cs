using Microsoft.Extensions.Logging.Console;

namespace StockQuoteAlert.Cli.Logging;

public sealed class CompactConsoleFormatterOptions : ConsoleFormatterOptions
{
    /// <summary>
    /// <see cref="LoggerColorBehavior.Default"/> colors the output only on a terminal and only when the
    /// <c>NO_COLOR</c> environment variable is not set.
    /// </summary>
    public LoggerColorBehavior ColorBehavior { get; set; }
}