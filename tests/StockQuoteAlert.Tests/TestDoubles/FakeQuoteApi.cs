using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text;

namespace StockQuoteAlert.Tests.TestDoubles;

/// <summary>
/// Simulates the Yahoo Finance chart API. Scripted responses are served in order; once they run out, the last
/// scripted price (22.63 by default) keeps being served. Every request is recorded, including retries.
/// </summary>
internal sealed class FakeQuoteApi : HttpMessageHandler
{
    public static readonly Uri BaseAddress = new("https://yahoo.test/");

    public const decimal DefaultPrice = 22.63m;

    private readonly ConcurrentQueue<Func<CancellationToken, Task<HttpResponseMessage>>> _scripted = new();
    private Func<CancellationToken, Task<HttpResponseMessage>> _fallback = _ => Task.FromResult(PriceResponse(DefaultPrice));

    public ConcurrentQueue<ReceivedRequest> Requests { get; } = new();

    /// <summary>Released once per request, so tests can wait for a background check without sleeping.</summary>
    public SemaphoreSlim RequestSignal { get; } = new(0);

    public ReceivedRequest LastRequest => Requests.Last();

    public static string Payload(decimal price) => Payload(price, TestData.Now);

    public static string Payload(decimal price, DateTimeOffset quotedAt) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $$$"""{"chart":{"result":[{"meta":{"currency":"BRL","symbol":"PETR4.SA","regularMarketPrice":{{{price}}},"regularMarketTime":{{{quotedAt.ToUnixTimeSeconds()}}}}}],"error":null}}""");

    public HttpClient CreateClient() => new(this, disposeHandler: false) { BaseAddress = BaseAddress };

    public FakeQuoteApi RespondWithPrices(params decimal[] prices)
    {
        foreach (var price in prices)
        {
            Enqueue(() => PriceResponse(price));
        }

        _fallback = _ => Task.FromResult(PriceResponse(prices[^1]));
        return this;
    }

    public FakeQuoteApi RespondWithStatus(HttpStatusCode statusCode)
    {
        Enqueue(() => new HttpResponseMessage(statusCode));
        return this;
    }

    public FakeQuoteApi RespondWithBody(string json)
    {
        Enqueue(() => JsonResponse(json));
        return this;
    }

    public FakeQuoteApi FailWith(Exception exception)
    {
        Enqueue(() => throw exception);
        return this;
    }

    /// <summary>Answers with a price only after <paramref name="delay"/>, unless the request is cancelled first.</summary>
    public FakeQuoteApi RespondSlowly(TimeSpan delay)
    {
        _scripted.Enqueue(async cancellationToken =>
        {
            await Task.Delay(delay, cancellationToken);
            return PriceResponse(DefaultPrice);
        });
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Requests.Enqueue(new ReceivedRequest(request.Method, request.RequestUri!, request.Headers.UserAgent.ToString()));

        try
        {
            var next = _scripted.TryDequeue(out var scripted) ? scripted : _fallback;
            return next(cancellationToken);
        }
        finally
        {
            RequestSignal.Release();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            RequestSignal.Dispose();
        }

        base.Dispose(disposing);
    }

    private void Enqueue(Func<HttpResponseMessage> respond) => _scripted.Enqueue(_ => Task.FromResult(respond()));

    private static HttpResponseMessage PriceResponse(decimal price) => JsonResponse(Payload(price));

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    public sealed record ReceivedRequest(HttpMethod Method, Uri Uri, string UserAgent);
}