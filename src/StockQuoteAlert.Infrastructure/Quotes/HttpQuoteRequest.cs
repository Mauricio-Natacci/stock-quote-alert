using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Polly;
using StockQuoteAlert.Core.Application.Exceptions;
using StockQuoteAlert.Core.Domain;

namespace StockQuoteAlert.Infrastructure.Quotes;

/// <summary>
/// Sends a quote request and reads its JSON body, translating every HTTP failure into the
/// <see cref="QuoteProviderException"/> contract shared by all quote sources.
/// </summary>
internal static class HttpQuoteRequest
{
    /// <param name="rejects">Maps a status code to an exception the source wants to raise instead of the default one.</param>
    /// <exception cref="TickerNotFoundException">HTTP 404.</exception>
    /// <exception cref="QuoteProviderException">
    /// Recoverable for network errors, timeouts, resilience rejections, malformed JSON and unexpected status codes;
    /// whatever <paramref name="rejects"/> returns otherwise.
    /// </exception>
    public static async Task<T?> GetJsonAsync<T>(
        HttpClient httpClient,
        HttpRequestMessage request,
        JsonTypeInfo<T> responseType,
        string source,
        Ticker ticker,
        Func<HttpStatusCode, QuoteProviderException?>? rejects,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                throw new TickerNotFoundException(ticker);
            }

            if (rejects?.Invoke(response.StatusCode) is { } rejection)
            {
                throw rejection;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new QuoteProviderException($"{source} responded with HTTP {(int)response.StatusCode} for {ticker}.");
            }

            return await response.Content.ReadFromJsonAsync(responseType, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            throw new QuoteProviderException($"Could not reach {source} for {ticker}: {exception.Message}", exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new QuoteProviderException($"{source} did not answer in time for {ticker}.", exception);
        }
        catch (ExecutionRejectedException exception)
        {
            // Raised by the resilience pipeline: an attempt timed out or the circuit breaker is open.
            throw new QuoteProviderException($"{source} is unavailable for {ticker}: {exception.Message}", exception);
        }
        catch (JsonException exception)
        {
            throw new QuoteProviderException($"{source} returned an invalid response for {ticker}.", exception);
        }
    }
}