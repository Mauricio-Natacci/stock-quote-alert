using System.Reflection;
using Microsoft.Extensions.Options;
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
    public async Task ValidArgumentsWithShippedConfiguration_FailsAtStartupValidation()
    {
        // appsettings.json leaves the addresses and credentials empty for the user to fill in.
        // Turning configuration errors into a clean message and exit code is not handled yet.
        var exception = await Assert.ThrowsAsync<AggregateException>(() => RunAsync("PETR4", "22.67", "22.59"));

        var failures = exception.InnerExceptions.Cast<OptionsValidationException>().SelectMany(inner => inner.Failures).ToList();
        Assert.Contains(failures, failure => failure.Contains("Email:Recipient is required.", StringComparison.Ordinal));
        Assert.Contains(failures, failure => failure.Contains("Smtp:Username is required", StringComparison.Ordinal));
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