using StockQuoteAlert.Core.Domain;

namespace StockQuoteAlert.Tests.TestDoubles;

internal static class TestData
{
    public static readonly DateTimeOffset Now = new(2026, 10, 29, 17, 30, 0, TimeSpan.Zero);

    public static Ticker Petr4 { get; } = Ticker.Create("PETR4");

    public static AlertThresholds Thresholds { get; } = new(sellAbove: 22.67m, buyBelow: 22.59m);

    public static Quote QuoteAt(decimal price) => new(Petr4, price, Now, "Yahoo Finance");
}