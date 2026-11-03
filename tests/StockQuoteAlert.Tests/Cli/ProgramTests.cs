namespace StockQuoteAlert.Tests.Cli;

public sealed class ProgramTests
{
    [Fact]
    public void AssemblyName_IsStockQuoteAlert()
    {
        string? name = typeof(Program).Assembly.GetName().Name;

        Assert.Equal("stock-quote-alert", name);
    }
}