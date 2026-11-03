using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Polly;
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

    public string Name => "Yahoo Finance";

    public async Task<RawQuote> FetchAsync(Ticker ticker, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ticker);

        var path = $"v8/finance/chart/{Uri.EscapeDataString(ticker.Symbol + B3Suffix)}?interval=1d&range=1d";
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        request.Headers.UserAgent.ParseAdd(options.Value.UserAgent);

        YahooChartResponse? payload;

        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                throw new TickerNotFoundException(ticker);
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new QuoteProviderException($"{Name} responded with HTTP {(int)response.StatusCode} for {ticker}.");
            }

            payload = await response.Content
                .ReadFromJsonAsync(YahooJsonContext.Default.YahooChartResponse, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            throw new QuoteProviderException($"Could not reach {Name} for {ticker}: {exception.Message}", exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new QuoteProviderException($"{Name} did not answer in time for {ticker}.", exception);
        }
        catch (ExecutionRejectedException exception)
        {
            // Raised by the resilience pipeline: an attempt timed out or the circuit breaker is open.
            throw new QuoteProviderException($"{Name} is unavailable for {ticker}: {exception.Message}", exception);
        }
        catch (JsonException exception)
        {
            throw new QuoteProviderException($"{Name} returned an invalid response for {ticker}.", exception);
        }

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