using StockQuoteAlert.Core.Application.Journal;
using StockQuoteAlert.Core.Domain;

namespace StockQuoteAlert.Cli.Dashboard;

internal sealed record QuoteResponse(string Ticker, decimal Price, PriceZone Zone, string Source, DateTimeOffset QuotedAt, DateTimeOffset ObservedAt)
{
    public static QuoteResponse From(QuoteObservation observation) => new(
        observation.Quote.Ticker.Symbol,
        observation.Quote.Price,
        observation.Zone,
        observation.Quote.Source,
        observation.Quote.QuotedAt,
        observation.ObservedAt);
}