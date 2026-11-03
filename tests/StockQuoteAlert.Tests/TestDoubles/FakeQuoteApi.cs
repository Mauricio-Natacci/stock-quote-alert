using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text;

namespace StockQuoteAlert.Tests.TestDoubles;

/// <summary>
/// Simulates a quote HTTP API: Yahoo Finance by default, or brapi.dev through <see cref="Brapi"/>. Responses can be scripted for every ticker at once or for a specific
/// ticker, which then answers independently of the others. Scripted responses are served in order; once they run
/// out, the last scripted price (22.63 by default) keeps being served. Every request is recorded, including retries.
/// </summary>
internal sealed class FakeQuoteApi : HttpMessageHandler
{
    public static readonly Uri BaseAddress = new("https://yahoo.test/");

    public const decimal DefaultPrice = 22.63m;

    private readonly ConcurrentDictionary<string, Script> _byTicker = new(StringComparer.Ordinal);
    private readonly Func<decimal, string> _payload;
    private readonly Script _default;

    public FakeQuoteApi()
        : this(Payload)
    {
    }

    private FakeQuoteApi(Func<decimal, string> payload)
    {
        _payload = payload;
        _default = new Script(_ => Task.FromResult(PriceResponse(DefaultPrice)));
    }

    /// <summary>Answers like brapi.dev (<c>api/quote/{TICKER}</c>, ISO time).</summary>
    public static FakeQuoteApi Brapi() => new(BrapiPayload);

    public ConcurrentQueue<ReceivedRequest> Requests { get; } = new();

    /// <summary>Released once per request, so tests can wait for a background check without sleeping.</summary>
    public SemaphoreSlim RequestSignal { get; } = new(0);

    public ReceivedRequest LastRequest => Requests.Last();

    public static string Payload(decimal price) => Payload(price, TestData.Now);

    public static string Payload(decimal price, DateTimeOffset quotedAt) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $$$"""{"chart":{"result":[{"meta":{"currency":"BRL","symbol":"PETR4.SA","regularMarketPrice":{{{price}}},"regularMarketTime":{{{quotedAt.ToUnixTimeSeconds()}}}}}],"error":null}}""");

    public static string BrapiPayload(decimal price) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $$$"""{"results":[{"symbol":"PETR4","currency":"BRL","regularMarketPrice":{{{price}}},"regularMarketTime":"{{{TestData.Now:O}}}"}]}""");

    public HttpClient CreateClient() => new(this, disposeHandler: false) { BaseAddress = BaseAddress };

    /// <summary>Number of requests made for one ticker (e.g. "PETR4").</summary>
    public int RequestsFor(string ticker) => Requests.Count(request => request.Ticker == ticker);

    public FakeQuoteApi RespondWithPrices(params decimal[] prices) => RespondWithPrices(_default, prices);

    public FakeQuoteApi RespondWithPricesFor(string ticker, params decimal[] prices) => RespondWithPrices(ScriptFor(ticker), prices);

    public FakeQuoteApi RespondWithStatus(HttpStatusCode statusCode) => Enqueue(_default, () => new HttpResponseMessage(statusCode));

    public FakeQuoteApi RespondWithStatusFor(string ticker, HttpStatusCode statusCode) => Enqueue(ScriptFor(ticker), () => new HttpResponseMessage(statusCode));

    public FakeQuoteApi RespondWithBody(string json) => Enqueue(_default, () => JsonResponse(json));

    public FakeQuoteApi FailWith(Exception exception) => Enqueue(_default, () => throw exception);

    /// <summary>Answers with a price only after <paramref name="delay"/>, unless the request is cancelled first.</summary>
    public FakeQuoteApi RespondSlowly(TimeSpan delay) => RespondSlowly(_default, delay);

    public FakeQuoteApi RespondSlowlyFor(string ticker, TimeSpan delay) => RespondSlowly(ScriptFor(ticker), delay);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Yahoo is asked for ".../chart/PETR4.SA" and brapi for ".../quote/PETR4".
        var ticker = request.RequestUri!.Segments[^1].Split('.')[0];
        Requests.Enqueue(new ReceivedRequest(
            request.Method, request.RequestUri, request.Headers.UserAgent.ToString(), ticker, request.Headers.Authorization?.ToString()));

        try
        {
            var script = _byTicker.TryGetValue(ticker, out var tickerScript) ? tickerScript : _default;
            return script.Next()(cancellationToken);
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

    private HttpResponseMessage PriceResponse(decimal price) => JsonResponse(_payload(price));

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private Script ScriptFor(string ticker) => _byTicker.GetOrAdd(ticker, _ => new Script(_default.Fallback));

    private FakeQuoteApi RespondWithPrices(Script script, decimal[] prices)
    {
        foreach (var price in prices)
        {
            Enqueue(script, () => PriceResponse(price));
        }

        script.Fallback = _ => Task.FromResult(PriceResponse(prices[^1]));
        return this;
    }

    private FakeQuoteApi RespondSlowly(Script script, TimeSpan delay)
    {
        script.Scripted.Enqueue(async cancellationToken =>
        {
            await Task.Delay(delay, cancellationToken);
            return PriceResponse(DefaultPrice);
        });
        return this;
    }

    private FakeQuoteApi Enqueue(Script script, Func<HttpResponseMessage> respond)
    {
        script.Scripted.Enqueue(_ => Task.FromResult(respond()));
        return this;
    }

    public sealed record ReceivedRequest(HttpMethod Method, Uri Uri, string UserAgent, string Ticker, string? Authorization = null);

    private sealed class Script(Func<CancellationToken, Task<HttpResponseMessage>> fallback)
    {
        public ConcurrentQueue<Func<CancellationToken, Task<HttpResponseMessage>>> Scripted { get; } = new();

        public Func<CancellationToken, Task<HttpResponseMessage>> Fallback { get; set; } = fallback;

        public Func<CancellationToken, Task<HttpResponseMessage>> Next() => Scripted.TryDequeue(out var scripted) ? scripted : Fallback;
    }
}