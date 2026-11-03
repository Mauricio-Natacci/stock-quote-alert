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
            "error: Expected 3 arguments (TICKER SELL_PRICE BUY_PRICE) but received 2 arguments." + Environment.NewLine + Environment.NewLine,
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
    public void ValidArguments_PrintsPlaceholderAndSucceeds()
    {
        var exitCode = Run("petr4", "22,67", "22.59");

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal(
            "Monitoring PETR4 (sell above 22.67, buy below 22.59): not implemented yet." + Environment.NewLine,
            _out.ToString());
        Assert.Empty(_error.ToString());
    }

    public void Dispose()
    {
        Console.SetOut(_originalOut);
        Console.SetError(_originalError);
        _out.Dispose();
        _error.Dispose();
    }

    private static int Run(params string[] args)
    {
        var entryPoint = typeof(ExitCodes).Assembly.EntryPoint!;

        return (int)entryPoint.Invoke(null, BindingFlags.DoNotWrapExceptions, null, [args], null)!;
    }
}

[CollectionDefinition(nameof(ConsoleSensitive), DisableParallelization = true)]
public sealed class ConsoleSensitive;