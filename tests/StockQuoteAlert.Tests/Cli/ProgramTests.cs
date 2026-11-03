using System.Reflection;
using StockQuoteAlert.Cli;
using StockQuoteAlert.Cli.CommandLine;

namespace StockQuoteAlert.Tests.Cli;

[Collection(nameof(ConsoleSensitive))]
public sealed class ProgramTests : IDisposable
{
    private readonly TextWriter _originalOut = Console.Out;
    private readonly TextWriter _originalError = Console.Error;
    private readonly StringWriter _out = new();
    private readonly StringWriter _error = new();

    public ProgramTests()
    {
        Console.SetOut(_out);
        Console.SetError(_error);
    }

    [Fact]
    public void AssemblyName_IsStockQuoteAlert()
    {
        string? name = typeof(ExitCodes).Assembly.GetName().Name;

        Assert.Equal("stock-quote-alert", name);
    }

    [Fact]
    public void Help_PrintsUsageToStandardOutputAndSucceeds()
    {
        var exitCode = Run("--help");

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal(CommandLineParser.Usage + Environment.NewLine, _out.ToString());
        Assert.Empty(_error.ToString());
    }

    [Fact]
    public void WrongArgumentCount_PrintsErrorAndUsageToStandardErrorAndFails()
    {
        var exitCode = Run("PETR4", "22.67");

        Assert.Equal(ExitCodes.InvalidArguments, exitCode);
        Assert.StartsWith(
            "error: Expected TICKER SELL_PRICE BUY_PRICE (3 arguments per stock) but received 2 arguments." + Environment.NewLine + Environment.NewLine,
            _error.ToString(),
            StringComparison.Ordinal);
        Assert.Contains(CommandLineParser.Usage, _error.ToString(), StringComparison.Ordinal);
        Assert.Empty(_out.ToString());
    }

    [Fact]
    public void SeveralInvalidArguments_PrintsOneErrorLineEach()
    {
        var exitCode = Run("X", "a", "b");

        Assert.Equal(ExitCodes.InvalidArguments, exitCode);
        var errorLines = _error.ToString().Split(Environment.NewLine).Where(line => line.StartsWith("error: ", StringComparison.Ordinal));
        Assert.Equal(3, errorLines.Count());
    }

    [Fact]
    public void SeveralStocksWithErrors_PrintsEachErrorWithItsStock()
    {
        var exitCode = Run("PETR4", "22.67", "22.59", "VAL3", "61.50", "60.80", "PETR4", "30", "20");

        Assert.Equal(ExitCodes.InvalidArguments, exitCode);
        var errorLines = _error.ToString().Split(Environment.NewLine).Where(line => line.StartsWith("error: ", StringComparison.Ordinal)).ToList();
        Assert.Equal(
            [
                "error: Stock 2 (VAL3 61.50 60.80): Invalid ticker 'VAL3'. Expected a B3 ticker such as PETR4 or BOVA11.",
                "error: PETR4 was given more than once. Each stock must appear a single time.",
            ],
            errorLines);
    }

    [Fact]
    public async Task ShippedConfiguration_PrintsWhatIsMissingAndExitsWithInvalidConfiguration()
    {
        // appsettings.json leaves the addresses and credentials empty for the user to fill in.
        var exitCode = await RunAsync("PETR4", "22.67", "22.59");

        Assert.Equal(ExitCodes.InvalidConfiguration, exitCode);
        Assert.Equal(
            """
            error: invalid configuration
              - Email:Recipient is required.
              - Email:SenderAddress is required.
              - Smtp:Username is required when Smtp:RequireAuthentication is true.
              - Smtp:Password is required when Smtp:RequireAuthentication is true (set it through user-secrets or the Smtp__Password environment variable).

            Set the values in appsettings.json, user-secrets or environment variables (e.g. Smtp__Password).

            """,
            _error.ToString(),
            ignoreLineEndingDifferences: true);
        Assert.Empty(_out.ToString());
    }

    [Fact]
    public async Task ValueThatCannotBeRead_IsReportedAsInvalidConfiguration()
    {
        Environment.SetEnvironmentVariable("Smtp__Port", "abc");

        try
        {
            var exitCode = await RunAsync("PETR4", "22.67", "22.59");

            Assert.Equal(ExitCodes.InvalidConfiguration, exitCode);
            Assert.Contains("  - Failed to convert configuration value 'abc' at 'Smtp:Port' to type 'System.Int32'.", _error.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("   at ", _error.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("Smtp__Port", null);
        }
    }

    public void Dispose()
    {
        Console.SetOut(_originalOut);
        Console.SetError(_originalError);
        _out.Dispose();
        _error.Dispose();
    }

    private static int Run(params string[] args) => RunAsync(args).GetAwaiter().GetResult();

    private static Task<int> RunAsync(params string[] args)
    {
        var entryPoint = typeof(ExitCodes).Assembly.EntryPoint!;
        var result = entryPoint.Invoke(null, BindingFlags.DoNotWrapExceptions, null, [args], null);

        return result as Task<int> ?? Task.FromResult((int)result!);
    }
}

[CollectionDefinition(nameof(ConsoleSensitive), DisableParallelization = true)]
public sealed class ConsoleSensitive;