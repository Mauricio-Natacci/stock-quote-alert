using StockQuoteAlert.Core.Formatting;

namespace StockQuoteAlert.Tests.Core.Formatting;

public sealed class BrazilianFormatTests
{
    private static readonly DateTimeOffset Moment = new(2026, 10, 29, 17, 30, 5, TimeSpan.Zero);

    public static TheoryData<decimal, string> Amounts => new()
    {
        { 22.6m, "R$ 22,60" },
        { 0m, "R$ 0,00" },
        { 1234.5m, "R$ 1.234,50" },
        { 1234567.891m, "R$ 1.234.567,89" },
        { 0.015m, "R$ 0,02" },
        { 0.014m, "R$ 0,01" },
        { -1234.5m, "-R$ 1.234,50" },
        { -0.015m, "-R$ 0,02" },
    };

    public static TheoryData<decimal, string> Percentages => new()
    {
        { 0.1323m, "0,13%" },
        { 0m, "0,00%" },
        { 0.125m, "0,13%" },
        { 1234.5m, "1.234,50%" },
        { -2.5m, "-2,50%" },
    };

    [Theory]
    [MemberData(nameof(Amounts))]
    public void Currency_UsesBrazilianSeparators(decimal value, string expected)
    {
        Assert.Equal(expected, BrazilianFormat.Currency(value));
    }

    [Theory]
    [MemberData(nameof(Percentages))]
    public void Percent_UsesBrazilianSeparators(decimal value, string expected)
    {
        Assert.Equal(expected, BrazilianFormat.Percent(value));
    }

    [Fact]
    public void BrasiliaOffset_IsUtcMinusThree()
    {
        Assert.Equal(TimeSpan.FromHours(-3), BrazilianFormat.BrasiliaOffset);
    }

    [Fact]
    public void DateTime_IsShownInBrasiliaTime()
    {
        Assert.Equal("29/10/2026 14:30:05", BrazilianFormat.DateTime(Moment));
    }

    [Fact]
    public void DateTimeWithoutSeconds_IsShownInBrasiliaTime()
    {
        Assert.Equal("29/10/2026 14:30", BrazilianFormat.DateTimeWithoutSeconds(Moment));
    }

    [Fact]
    public void Time_IsShownInBrasiliaTime()
    {
        Assert.Equal("14:30:05", BrazilianFormat.Time(Moment));
    }

    [Fact]
    public void Dates_WithOtherOffsets_AreConverted()
    {
        Assert.Equal("29/10/2026 14:30:05", BrazilianFormat.DateTime(Moment.ToOffset(TimeSpan.FromHours(9))));
    }

    [Fact]
    public void DateTime_JustAfterUtcMidnight_ShowsPreviousDay()
    {
        var moment = new DateTimeOffset(2027, 1, 1, 1, 0, 0, TimeSpan.Zero);

        Assert.Equal("31/12/2026 22:00:00", BrazilianFormat.DateTime(moment));
    }

    [Fact]
    public void DateTime_InSouthernSummer_HasNoDaylightSavingShift()
    {
        var moment = new DateTimeOffset(2027, 1, 15, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal("15/01/2027 09:00:00", BrazilianFormat.DateTime(moment));
    }
}