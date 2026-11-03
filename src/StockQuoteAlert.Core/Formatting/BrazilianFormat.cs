using System.Globalization;

namespace StockQuoteAlert.Core.Formatting;

/// <summary>
/// pt-BR presentation helpers that do not depend on ICU culture data (the app runs with invariant globalization).
/// Brasília has had no daylight saving time since 2019, so a fixed UTC-3 offset is exact.
/// </summary>
public static class BrazilianFormat
{
    public static readonly TimeSpan BrasiliaOffset = TimeSpan.FromHours(-3);

    private static readonly NumberFormatInfo NumberFormat = NumberFormatInfo.ReadOnly(new NumberFormatInfo
    {
        NumberDecimalSeparator = ",",
        NumberGroupSeparator = ".",
    });

    /// <summary>Formats an amount in reais, rounded to cents (e.g. -1234.5 → "-R$ 1.234,50").</summary>
    public static string Currency(decimal value)
    {
        var amount = $"R$ {Math.Abs(value).ToString("N2", NumberFormat)}";

        return value < 0 ? $"-{amount}" : amount;
    }

    /// <summary>Formats a percentage value, rounded to two decimals (e.g. 0.13 → "0,13%").</summary>
    public static string Percent(decimal value) => $"{value.ToString("N2", NumberFormat)}%";

    public static string DateTime(DateTimeOffset value) => Format(value, "dd/MM/yyyy HH:mm:ss");

    public static string DateTimeWithoutSeconds(DateTimeOffset value) => Format(value, "dd/MM/yyyy HH:mm");

    public static string Time(DateTimeOffset value) => Format(value, "HH:mm:ss");

    private static string Format(DateTimeOffset value, string pattern) =>
        value.ToOffset(BrasiliaOffset).ToString(pattern, CultureInfo.InvariantCulture);
}