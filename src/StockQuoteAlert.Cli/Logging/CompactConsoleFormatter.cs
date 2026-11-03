using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using StockQuoteAlert.Core.Application.Monitoring;

namespace StockQuoteAlert.Cli.Logging;

/// <summary>
/// One line per event: time, colored level, the short category name (the class name) and the message. An exception
/// adds a second line with its type and message, without the stack trace. The end of a check of several stocks is
/// drawn as a separator, so the quotes of one check stay grouped.
/// </summary>
internal sealed class CompactConsoleFormatter(IOptionsMonitor<CompactConsoleFormatterOptions> options, TimeProvider timeProvider)
    : ConsoleFormatter(FormatterName)
{
    public const string FormatterName = "compact";

    private const int CategoryWidth = 21;
    private const string DefaultTimestampFormat = "HH:mm:ss ";
    private const string Dim = "\e[2m";
    private const string Reset = "\e[0m";

    private static readonly string Separator = new('-', 72);

    private static readonly Lazy<bool> TerminalSupportsColors =
        new(() => ShouldUseColors(Console.IsOutputRedirected, Environment.GetEnvironmentVariable("NO_COLOR")));

    public override void Write<TState>(in LogEntry<TState> logEntry, IExternalScopeProvider? scopeProvider, TextWriter textWriter)
    {
        ArgumentNullException.ThrowIfNull(textWriter);

        var current = options.CurrentValue;
        var useColors = current.ColorBehavior switch
        {
            LoggerColorBehavior.Enabled => true,
            LoggerColorBehavior.Disabled => false,
            _ => TerminalSupportsColors.Value,
        };

        if (logEntry.EventId.Name == MonitoringWorker.CheckCompletedEvent)
        {
            textWriter.WriteLine(Paint(Separator, Dim, useColors));
            return;
        }

        var message = logEntry.Formatter(logEntry.State, logEntry.Exception);

        if (string.IsNullOrEmpty(message) && logEntry.Exception is null)
        {
            return;
        }

        var now = current.UseUtcTimestamp ? timeProvider.GetUtcNow() : timeProvider.GetLocalNow();
        var timestamp = now.ToString(current.TimestampFormat ?? DefaultTimestampFormat, CultureInfo.InvariantCulture);

        textWriter.Write(Paint(timestamp, Dim, useColors));
        textWriter.Write(Paint(Level(logEntry.LogLevel), LevelColor(logEntry.LogLevel), useColors));
        textWriter.Write("  ");
        textWriter.Write(Paint(ShortCategory(logEntry.Category).PadRight(CategoryWidth), Dim, useColors));
        textWriter.Write("  ");
        textWriter.WriteLine(message);

        if (logEntry.Exception is { } exception)
        {
            textWriter.WriteLine(Paint($"  {exception.GetType().Name}: {exception.Message}", Dim, useColors));
        }
    }

    internal static bool ShouldUseColors(bool outputRedirected, string? noColor) =>
        !outputRedirected && string.IsNullOrEmpty(noColor);

    /// <summary>"StockQuoteAlert.Core.Application.Monitoring.StockMonitor" → "StockMonitor".</summary>
    internal static string ShortCategory(string category) => category[(category.LastIndexOf('.') + 1)..];

    private static string Paint(string text, string color, bool useColors) => useColors ? $"{color}{text}{Reset}" : text;

    private static string Level(LogLevel level) => level switch
    {
        LogLevel.Trace => "trce",
        LogLevel.Debug => "dbug",
        LogLevel.Information => "info",
        LogLevel.Warning => "warn",
        LogLevel.Error => "fail",
        _ => "crit",
    };

    private static string LevelColor(LogLevel level) => level switch
    {
        LogLevel.Information => "\e[32m",
        LogLevel.Warning => "\e[33m",
        LogLevel.Error => "\e[31m",
        LogLevel.Critical => "\e[1;31m",
        _ => Dim,
    };
}