using System.Globalization;
using StockQuoteAlert.Core.Domain;

namespace StockQuoteAlert.Cli.CommandLine;

internal static class CommandLineParser
{
    public const string Usage = """
        Usage: stock-quote-alert <TICKER> <SELL_PRICE> <BUY_PRICE>

          TICKER      B3 ticker to monitor (e.g. PETR4)
          SELL_PRICE  Sends a SELL alert when the price goes above this value
          BUY_PRICE   Sends a BUY alert when the price goes below this value

        Prices accept '.' or ',' as the decimal separator. SELL_PRICE must be greater than BUY_PRICE.

        Example:
          stock-quote-alert PETR4 22.67 22.59
        """;

    private const int ExpectedArgumentCount = 3;

    private static readonly string[] HelpFlags = ["-h", "--help", "-?", "/?"];

    public static CommandLineResult Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Any(arg => HelpFlags.Contains(arg, StringComparer.OrdinalIgnoreCase)))
        {
            return new CommandLineResult.HelpRequested();
        }

        if (args.Count != ExpectedArgumentCount)
        {
            return new CommandLineResult.Failure(
                [$"Expected 3 arguments (TICKER SELL_PRICE BUY_PRICE) but received {args.Count} argument{(args.Count == 1 ? "" : "s")}."]);
        }

        var (rawTicker, rawSell, rawBuy) = (args[0], args[1], args[2]);
        var errors = new List<string>();

        if (!Ticker.TryCreate(rawTicker, out var ticker))
        {
            errors.Add($"Invalid ticker '{rawTicker}'. Expected a B3 ticker such as PETR4 or BOVA11.");
        }

        if (!TryParsePrice(rawSell, out var sellPrice))
        {
            errors.Add($"Invalid sell price '{rawSell}'. Expected a positive number such as 22.67.");
        }

        if (!TryParsePrice(rawBuy, out var buyPrice))
        {
            errors.Add($"Invalid buy price '{rawBuy}'. Expected a positive number such as 22.59.");
        }

        if (errors.Count > 0)
        {
            return new CommandLineResult.Failure(errors);
        }

        if (sellPrice <= buyPrice)
        {
            return new CommandLineResult.Failure(
                [$"Sell price ({sellPrice}) must be greater than buy price ({buyPrice})."]);
        }

        return new CommandLineResult.Success(ticker!, new AlertThresholds(sellPrice, buyPrice));
    }

    private static bool TryParsePrice(string raw, out decimal price) =>
        decimal.TryParse(raw.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out price)
        && price > 0;
}