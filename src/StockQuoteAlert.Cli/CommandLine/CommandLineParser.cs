using System.Globalization;
using StockQuoteAlert.Core.Application.Monitoring;
using StockQuoteAlert.Core.Domain;

namespace StockQuoteAlert.Cli.CommandLine;

internal static class CommandLineParser
{
    public const string Usage = """
        Usage: stock-quote-alert <TICKER> <SELL_PRICE> <BUY_PRICE> [<TICKER> <SELL_PRICE> <BUY_PRICE> ...]

          TICKER      B3 ticker to monitor (e.g. PETR4)
          SELL_PRICE  Sends a SELL alert when the price goes above this value
          BUY_PRICE   Sends a BUY alert when the price goes below this value

        Repeat the three values to monitor more stocks; each alert is sent as a separate e-mail.
        Prices accept '.' or ',' as the decimal separator. SELL_PRICE must be greater than BUY_PRICE.

        Examples:
          stock-quote-alert PETR4 22.67 22.59
          stock-quote-alert PETR4 22.67 22.59 VALE3 61.50 60.80
        """;

    private const int ArgumentsPerStock = 3;

    private static readonly string[] HelpFlags = ["-h", "--help", "-?", "/?"];

    public static CommandLineResult Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Any(arg => HelpFlags.Contains(arg, StringComparer.OrdinalIgnoreCase)))
        {
            return new CommandLineResult.HelpRequested();
        }

        if (args.Count == 0 || args.Count % ArgumentsPerStock != 0)
        {
            return new CommandLineResult.Failure(
                [$"Expected TICKER SELL_PRICE BUY_PRICE (3 arguments per stock) but received {args.Count} argument{(args.Count == 1 ? "" : "s")}."]);
        }

        var groups = args.Chunk(ArgumentsPerStock).ToList();
        var errors = new List<string>();
        var targets = new List<MonitoringTarget>();

        for (var i = 0; i < groups.Count; i++)
        {
            // With a single stock the errors are already unambiguous; with several, say which stock they refer to.
            var prefix = groups.Count == 1 ? string.Empty : $"Stock {i + 1} ({string.Join(' ', groups[i])}): ";

            if (TryParseTarget(groups[i], out var target, out var targetErrors))
            {
                targets.Add(target);
            }
            else
            {
                errors.AddRange(targetErrors.Select(error => prefix + error));
            }
        }

        errors.AddRange(targets
            .GroupBy(target => target.Ticker)
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key} was given more than once. Each stock must appear a single time."));

        return errors.Count > 0
            ? new CommandLineResult.Failure(errors)
            : new CommandLineResult.Success(new MonitoringTargets(targets));
    }

    private static bool TryParseTarget(string[] group, out MonitoringTarget target, out List<string> errors)
    {
        var (rawTicker, rawSell, rawBuy) = (group[0], group[1], group[2]);
        errors = [];
        target = null!;

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
            return false;
        }

        if (sellPrice <= buyPrice)
        {
            errors.Add($"Sell price ({sellPrice}) must be greater than buy price ({buyPrice}).");
            return false;
        }

        target = new MonitoringTarget(ticker!, new AlertThresholds(sellPrice, buyPrice));
        return true;
    }

    private static bool TryParsePrice(string raw, out decimal price) =>
        decimal.TryParse(raw.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out price)
        && price > 0;
}