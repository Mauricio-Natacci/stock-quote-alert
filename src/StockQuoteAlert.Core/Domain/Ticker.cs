using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace StockQuoteAlert.Core.Domain;

/// <summary>
/// A B3 ticker symbol: a 4-character root starting with a letter (PETR, B3SA), a 1- or 2-digit share class
/// (3, 4, 11, 34) and an optional F for the odd-lot market. Stored in upper case.
/// </summary>
public sealed partial record Ticker
{
    private Ticker(string symbol) => Symbol = symbol;

    public string Symbol { get; }

    public static bool TryCreate(string? value, [NotNullWhen(true)] out Ticker? ticker)
    {
        var normalized = value?.Trim().ToUpperInvariant();

        if (normalized is null || !B3SymbolPattern().IsMatch(normalized))
        {
            ticker = null;
            return false;
        }

        ticker = new Ticker(normalized);
        return true;
    }

    public static Ticker Create(string value) =>
        TryCreate(value, out var ticker)
            ? ticker
            : throw new ArgumentException($"'{value}' is not a valid B3 ticker (e.g. PETR4, VALE3, BOVA11).", nameof(value));

    public override string ToString() => Symbol;

    [GeneratedRegex("^[A-Z][A-Z0-9]{3}[0-9]{1,2}F?$", RegexOptions.CultureInvariant)]
    private static partial Regex B3SymbolPattern();
}