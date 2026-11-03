using StockQuoteAlert.Core.Application.Abstractions;
using StockQuoteAlert.Core.Application.Exceptions;
using StockQuoteAlert.Core.Domain;

namespace StockQuoteAlert.Infrastructure.Quotes;

/// <summary>
/// Adapts an <see cref="IQuoteSource"/> to <see cref="IQuoteProvider"/>, turning its raw data into the domain
/// <see cref="Quote"/>: price rounded to cents, time in UTC, price must be positive. Source failures pass through.
/// </summary>
internal sealed class QuoteSourceAdapter(IQuoteSource source) : IQuoteProvider
{
    private const int B3PriceDecimals = 2;

    public async Task<Quote> GetQuoteAsync(Ticker ticker, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ticker);

        var raw = await source.FetchAsync(ticker, cancellationToken).ConfigureAwait(false);
        var price = Math.Round(raw.Price, B3PriceDecimals, MidpointRounding.AwayFromZero);

        if (price <= 0)
        {
            throw new QuoteProviderException($"{source.Name} returned an invalid price ({raw.Price}) for {ticker}.");
        }

        return new Quote(ticker, price, raw.QuotedAt.ToUniversalTime(), source.Name);
    }
}