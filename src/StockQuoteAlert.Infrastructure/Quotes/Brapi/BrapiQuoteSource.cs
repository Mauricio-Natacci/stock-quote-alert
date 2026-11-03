using System.Net;
using StockQuoteAlert.Core.Application.Exceptions;
using StockQuoteAlert.Core.Domain;

namespace StockQuoteAlert.Infrastructure.Quotes.Brapi;

/// <summary>
/// Reads the latest price from brapi.dev (<c>api/quote/{TICKER}</c>), a B3-specific API. A rejected token
/// (HTTP 401/403) is unrecoverable: retrying would repeat the same error.
/// </summary>
internal sealed class BrapiQuoteSource(HttpClient httpClient) : IQuoteSource
{
    public string Name => "brapi.dev";

    public async Task<RawQuote> FetchAsync(Ticker ticker, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ticker);

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"api/quote/{Uri.EscapeDataString(ticker.Symbol)}", UriKind.Relative));

        var payload = await HttpQuoteRequest
            .GetJsonAsync(httpClient, request, BrapiJsonContext.Default.BrapiQuoteResponse, Name, ticker, status => Reject(status, ticker), cancellationToken)
            .ConfigureAwait(false);

        if (payload?.Results is not [{ RegularMarketPrice: { } price, RegularMarketTime: { } quotedAt }, ..])
        {
            throw new QuoteProviderException($"{Name} returned no price for {ticker}.");
        }

        return new RawQuote(price, quotedAt);
    }

    private QuoteProviderException? Reject(HttpStatusCode status, Ticker ticker) => status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
        ? new QuoteProviderException(
            $"{Name} rejected the request for {ticker} (HTTP {(int)status}). Check QuoteProvider:Brapi:Token.",
            isRecoverable: false)
        : null;
}