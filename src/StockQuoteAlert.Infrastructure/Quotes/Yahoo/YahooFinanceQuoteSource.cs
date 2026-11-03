using Microsoft.Extensions.Options;
using StockQuoteAlert.Core.Application.Exceptions;
using StockQuoteAlert.Core.Domain;

namespace StockQuoteAlert.Infrastructure.Quotes.Yahoo;

/// <summary>
/// Reads the latest price from the Yahoo Finance chart API (<c>v8/finance/chart/{TICKER}.SA</c>). Expects an
/// <see cref="HttpClient"/> whose base address is the API root; sends the configured User-Agent on every request.
/// </summary>
internal sealed class YahooFinanceQuoteSource(HttpClient httpClient, IOptions<YahooFinanceOptions> options) : IQuoteSource
{
    private const string B3Suffix = ".SA";

    private static readonly long MinUnixSeconds = DateTimeOffset.MinValue.ToUnixTimeSeconds();

    private static readonly long MaxUnixSeconds = DateTimeOffset.MaxValue.ToUnixTimeSeconds();

    internal const string DisplayName = "Yahoo Finance";

    public string Name => DisplayName;

    public async Task<RawQuote> FetchAsync(Ticker ticker, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ticker);

        var path = $"v8/finance/chart/{Uri.EscapeDataString(ticker.Symbol + B3Suffix)}?interval=1d&range=1d";
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        request.Headers.UserAgent.ParseAdd(options.Value.UserAgent);

        var payload = await HttpQuoteRequest
            .GetJsonAsync(httpClient, request, YahooJsonContext.Default.YahooChartResponse, Name, ticker, rejects: null, cancellationToken)
            .ConfigureAwait(false);

        if (payload?.Chart?.Result is not [{ Meta: { RegularMarketPrice: { } price, RegularMarketTime: { } unixTime } }, ..])
        {
            throw new QuoteProviderException($"{Name} returned no price for {ticker}.");
        }

        if (unixTime < MinUnixSeconds || unixTime > MaxUnixSeconds)
        {
            throw new QuoteProviderException($"{Name} returned an invalid quote time ({unixTime}) for {ticker}.");
        }

        return new RawQuote(price, DateTimeOffset.FromUnixTimeSeconds(unixTime));
    }
}