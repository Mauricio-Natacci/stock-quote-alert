using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using StockQuoteAlert.Cli.Logging;
using StockQuoteAlert.Tests.TestDoubles;

namespace StockQuoteAlert.Tests.Cli.Logging;

public sealed class CompactConsoleFormatterTests
{
    private const string MonitorCategory = "StockQuoteAlert.Core.Application.Monitoring.StockMonitor";

    // 17:30:05 UTC; the fake clock's local time zone is UTC.
    private readonly FakeTimeProvider _time = new(TestData.Now.AddSeconds(5));
    private readonly CompactConsoleFormatterOptions _options = new() { ColorBehavior = LoggerColorBehavior.Disabled };

    [Fact]
    public void Name_IsCompact()
    {
        Assert.Equal("compact", CreateFormatter().Name);
    }

    [Fact]
    public void Write_PrintsTimeLevelShortCategoryAndMessageOnOneLine()
    {
        var output = Write(LogLevel.Information, MonitorCategory, "PETR4 quoted at 22.63 by Yahoo Finance (Neutral)");

        Assert.Equal("17:30:05 info  StockMonitor           PETR4 quoted at 22.63 by Yahoo Finance (Neutral)" + Environment.NewLine, output);
    }

    [Theory]
    [InlineData(LogLevel.Trace, "trce")]
    [InlineData(LogLevel.Debug, "dbug")]
    [InlineData(LogLevel.Information, "info")]
    [InlineData(LogLevel.Warning, "warn")]
    [InlineData(LogLevel.Error, "fail")]
    [InlineData(LogLevel.Critical, "crit")]
    public void Write_UsesFourLetterLevelNames(LogLevel level, string expected)
    {
        Assert.StartsWith($"17:30:05 {expected}  ", Write(level, MonitorCategory, "message"), StringComparison.Ordinal);
    }

    [Fact]
    public void Write_AlignsMessagesForCategoriesOfDifferentLengths()
    {
        var worker = Write(LogLevel.Information, "StockQuoteAlert.Core.Application.Monitoring.MonitoringWorker", "message");
        var notifier = Write(LogLevel.Information, "StockQuoteAlert.Infrastructure.Email.EmailAlertNotifier", "message");

        Assert.Equal(worker.IndexOf("message", StringComparison.Ordinal), notifier.IndexOf("message", StringComparison.Ordinal));
    }

    [Fact]
    public void Write_LongCategory_IsNotCut()
    {
        var output = Write(LogLevel.Information, "Some.Namespace.AVeryLongCategoryNameIndeed", "message");

        Assert.Contains("AVeryLongCategoryNameIndeed  message", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_WithException_AddsItsTypeAndMessageWithoutStackTrace()
    {
        var output = Write(LogLevel.Error, MonitorCategory, "Failed to check PETR4; retrying on next tick", ThrownException());

        var lines = output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.EndsWith("Failed to check PETR4; retrying on next tick", lines[0], StringComparison.Ordinal);
        Assert.Equal("  IOException: Connection refused", lines[1]);
    }

    [Fact]
    public void Write_EmptyMessageWithoutException_PrintsNothing()
    {
        Assert.Empty(Write(LogLevel.Information, MonitorCategory, ""));
    }

    [Fact]
    public void Write_EmptyMessageWithException_StillPrintsTheException()
    {
        Assert.Contains("IOException: Connection refused", Write(LogLevel.Error, MonitorCategory, "", ThrownException()), StringComparison.Ordinal);
    }

    [Fact]
    public void Write_UsesConfiguredTimestampFormat()
    {
        _options.TimestampFormat = "yyyy-MM-dd HH:mm:ss.fff ";

        Assert.StartsWith("2026-10-29 17:30:05.000 info", Write(LogLevel.Information, MonitorCategory, "message"), StringComparison.Ordinal);
    }

    [Fact]
    public void Write_UsesLocalTimeUnlessUtcIsRequested()
    {
        _time.SetLocalTimeZone(TimeZoneInfo.CreateCustomTimeZone("Brasilia", TimeSpan.FromHours(-3), "Brasilia", "Brasilia"));

        var local = Write(LogLevel.Information, MonitorCategory, "message");
        _options.UseUtcTimestamp = true;
        var utc = Write(LogLevel.Information, MonitorCategory, "message");

        Assert.StartsWith("14:30:05 ", local, StringComparison.Ordinal);
        Assert.StartsWith("17:30:05 ", utc, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(LogLevel.Information, "\e[32minfo\e[0m")]
    [InlineData(LogLevel.Warning, "\e[33mwarn\e[0m")]
    [InlineData(LogLevel.Error, "\e[31mfail\e[0m")]
    [InlineData(LogLevel.Critical, "\e[1;31mcrit\e[0m")]
    [InlineData(LogLevel.Debug, "\e[2mdbug\e[0m")]
    public void Write_WithColors_ColorsTheLevel(LogLevel level, string expected)
    {
        _options.ColorBehavior = LoggerColorBehavior.Enabled;

        var output = Write(level, MonitorCategory, "message");

        Assert.Contains(expected, output, StringComparison.Ordinal);
        Assert.StartsWith("\e[2m17:30:05 \e[0m", output, StringComparison.Ordinal);
        Assert.Contains("\e[2mStockMonitor", output, StringComparison.Ordinal);
        Assert.EndsWith("  message" + Environment.NewLine, output, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_WithoutColors_HasNoEscapeCodes()
    {
        Assert.DoesNotContain("\e[", Write(LogLevel.Critical, MonitorCategory, "message", ThrownException()), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, null, true)]
    [InlineData(false, "", true)]
    [InlineData(true, null, false)]
    [InlineData(false, "1", false)]
    [InlineData(true, "1", false)]
    public void ShouldUseColors_OnlyOnATerminalWithoutNoColor(bool outputRedirected, string? noColor, bool expected)
    {
        Assert.Equal(expected, CompactConsoleFormatter.ShouldUseColors(outputRedirected, noColor));
    }

    [Theory]
    [InlineData("StockQuoteAlert.Core.Application.Monitoring.StockMonitor", "StockMonitor")]
    [InlineData("Polly", "Polly")]
    [InlineData("System.Net.Http.HttpClient.YahooFinanceQuoteSource.LogicalHandler", "LogicalHandler")]
    public void ShortCategory_KeepsTheLastSegment(string category, string expected)
    {
        Assert.Equal(expected, CompactConsoleFormatter.ShortCategory(category));
    }

    private static IOException ThrownException()
    {
        try
        {
            throw new IOException("Connection refused");
        }
        catch (IOException exception)
        {
            return exception;
        }
    }

    private CompactConsoleFormatter CreateFormatter() => new(new FixedOptionsMonitor(_options), _time);

    private string Write(LogLevel level, string category, string message, Exception? exception = null)
    {
        using var writer = new StringWriter();
        var entry = new LogEntry<string>(level, category, new EventId(1), message, exception, (state, _) => state);

        CreateFormatter().Write(in entry, scopeProvider: null, writer);

        return writer.ToString();
    }

    private sealed class FixedOptionsMonitor(CompactConsoleFormatterOptions value) : IOptionsMonitor<CompactConsoleFormatterOptions>
    {
        public CompactConsoleFormatterOptions CurrentValue => value;

        public CompactConsoleFormatterOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<CompactConsoleFormatterOptions, string?> listener) => null;
    }
}