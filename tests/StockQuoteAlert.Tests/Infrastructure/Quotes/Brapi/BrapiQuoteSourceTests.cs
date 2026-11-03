using System.Net;
using Polly.CircuitBreaker;
using Polly.Timeout;
using StockQuoteAlert.Core.Application.Exceptions;
using StockQuoteAlert.Core.Domain;
using StockQuoteAlert.Infrastructure.Quotes.Brapi;
using StockQuoteAlert.Tests.TestDoubles;

namespace StockQuoteAlert.Tests.Infrastructure.Quotes.Brapi;

public sealed class BrapiQuoteSourceTests : IDisposable
{
    private readonly FakeQuoteApi _api = FakeQuoteApi.Brapi();
    private readonly HttpClient _client;
    private readonly BrapiQuoteSource _source;

    public BrapiQuoteSourceTests()
    {
        _client = _api.CreateClient();
        _source = new BrapiQuoteSource(_client);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Name_IsBrapi()
    {
        Assert.Equal("brapi.dev", _source.Name);
    }

    [Fact]
    public async Task FetchAsync_ReturnsPriceAndTimeAsReported()
    {
        _api.RespondWithBody("""
            {"results":[{"symbol":"PETR4","currency":"BRL","regularMarketPrice":22.6149,"regularMarketTime":"2026-10-29T13:54:30-03:00"}]}
            """);

        var quote = await _source.FetchAsync(TestData.Petr4, Ct);

        Assert.Equal(22.6149m, quote.Price);
        Assert.Equal(new DateTimeOffset(2026, 10, 29, 13, 54, 30, TimeSpan.FromHours(-3)), quote.QuotedAt);
    }

    [Fact]
    public async Task FetchAsync_ReadsFirstResultOnly()
    {
        _api.RespondWithBody("""
            {"results":[{"regularMarketPrice":22.61,"regularMarketTime":"2026-10-29T16:54:30Z"},{"regularMarketPrice":99,"regularMarketTime":"2026-10-29T16:54:30Z"}]}
            """);

        Assert.Equal(22.61m, (await _source.FetchAsync(TestData.Petr4, Ct)).Price);
    }

    [Fact]
    public async Task FetchAsync_RequestsTheQuoteForTheTickerWithoutSuffix()
    {
        await _source.FetchAsync(Ticker.Create("bova11"), Ct);

        var request = _api.LastRequest;
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("https://yahoo.test/api/quote/BOVA11", request.Uri.ToString());
    }

    [Fact]
    public async Task FetchAsync_WhenNotFound_ThrowsTickerNotFound()
    {
        _api.RespondWithStatus(HttpStatusCode.NotFound);

        var exception = await Assert.ThrowsAsync<TickerNotFoundException>(() => _source.FetchAsync(TestData.Petr4, Ct));

        Assert.Equal(TestData.Petr4, exception.Ticker);
        Assert.False(exception.IsRecoverable);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task FetchAsync_WhenTokenIsRejected_ThrowsUnrecoverableException(HttpStatusCode statusCode)
    {
        _api.RespondWithStatus(statusCode);

        var exception = await Assert.ThrowsAsync<QuoteProviderException>(() => _source.FetchAsync(TestData.Petr4, Ct));

        Assert.False(exception.IsRecoverable);
        Assert.IsNotType<TickerNotFoundException>(exception);
        Assert.Equal($"brapi.dev rejected the request for PETR4 (HTTP {(int)statusCode}). Check QuoteProvider:Brapi:Token.", exception.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.PaymentRequired)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task FetchAsync_WhenStatusIsOtherwiseNotSuccess_ThrowsRecoverableException(HttpStatusCode statusCode)
    {
        _api.RespondWithStatus(statusCode);

        var exception = await Assert.ThrowsAsync<QuoteProviderException>(() => _source.FetchAsync(TestData.Petr4, Ct));

        Assert.True(exception.IsRecoverable);
        Assert.Equal($"brapi.dev responded with HTTP {(int)statusCode} for PETR4.", exception.Message);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("""{"results":null}""")]
    [InlineData("""{"results":[]}""")]
    [InlineData("""{"results":[{"regularMarketTime":"2026-10-29T16:54:30Z"}]}""")]
    [InlineData("""{"results":[{"regularMarketPrice":22.61}]}""")]
    [InlineData("""{"error":true,"message":"Não encontramos a ação"}""")]
    public async Task FetchAsync_WhenPayloadHasNoPrice_ThrowsRecoverableException(string payload)
    {
        _api.RespondWithBody(payload);

        var exception = await Assert.ThrowsAsync<QuoteProviderException>(() => _source.FetchAsync(TestData.Petr4, Ct));

        Assert.True(exception.IsRecoverable);
        Assert.Equal("brapi.dev returned no price for PETR4.", exception.Message);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"results":[{"regularMarketPrice":22.61,"regularMarketTime":"yesterday"}]}""")]
    public async Task FetchAsync_WhenPayloadIsInvalidJson_ThrowsRecoverableException(string payload)
    {
        _api.RespondWithBody(payload);

        var exception = await Assert.ThrowsAsync<QuoteProviderException>(() => _source.FetchAsync(TestData.Petr4, Ct));

        Assert.True(exception.IsRecoverable);
        Assert.Equal("brapi.dev returned an invalid response for PETR4.", exception.Message);
    }

    public static TheoryData<Exception, string> TransportFailures => new()
    {
        { new HttpRequestException("Connection refused"), "Could not reach brapi.dev for PETR4: Connection refused" },
        { new TaskCanceledException("timeout", new TimeoutException()), "brapi.dev did not answer in time for PETR4." },
        { new TimeoutRejectedException("timed out"), "brapi.dev is unavailable for PETR4: timed out" },
        { new BrokenCircuitException("open"), "brapi.dev is unavailable for PETR4: open" },
    };

    [Theory]
    [MemberData(nameof(TransportFailures))]
    public async Task FetchAsync_WhenTransportFails_ThrowsRecoverableException(Exception failure, string expectedMessage)
    {
        _api.FailWith(failure);

        var exception = await Assert.ThrowsAsync<QuoteProviderException>(() => _source.FetchAsync(TestData.Petr4, Ct));

        Assert.True(exception.IsRecoverable);
        Assert.Same(failure, exception.InnerException);
        Assert.Equal(expectedMessage, exception.Message);
    }

    [Fact]
    public async Task FetchAsync_WhenCancelled_PropagatesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _source.FetchAsync(TestData.Petr4, cancellation.Token));
    }

    [Fact]
    public async Task FetchAsync_WithNullTicker_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _source.FetchAsync(null!, Ct));
    }

    public void Dispose()
    {
        _client.Dispose();
        _api.Dispose();
    }
}