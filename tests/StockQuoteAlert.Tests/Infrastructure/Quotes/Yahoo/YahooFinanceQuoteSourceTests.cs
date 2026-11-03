using System.Net;
using Microsoft.Extensions.Options;
using Polly.CircuitBreaker;
using Polly.Timeout;
using StockQuoteAlert.Core.Application.Exceptions;
using StockQuoteAlert.Core.Domain;
using StockQuoteAlert.Infrastructure.Quotes.Yahoo;
using StockQuoteAlert.Tests.TestDoubles;

namespace StockQuoteAlert.Tests.Infrastructure.Quotes.Yahoo;

public sealed class YahooFinanceQuoteSourceTests : IDisposable
{
    private readonly FakeQuoteApi _api = new();
    private readonly HttpClient _client;
    private readonly YahooFinanceQuoteSource _source;

    public YahooFinanceQuoteSourceTests()
    {
        _client = _api.CreateClient();
        _source = new YahooFinanceQuoteSource(_client, Options.Create(new YahooFinanceOptions()));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Name_IsYahooFinance()
    {
        Assert.Equal("Yahoo Finance", _source.Name);
    }

    [Fact]
    public async Task FetchAsync_ReturnsPriceAndTimeAsReported()
    {
        _api.RespondWithBody("""
            {"chart":{"result":[{"meta":{"currency":"BRL","symbol":"PETR4.SA","regularMarketPrice":22.6149,"regularMarketTime":1793292012}}],"error":null}}
            """);

        var quote = await _source.FetchAsync(TestData.Petr4, Ct);

        Assert.Equal(22.6149m, quote.Price);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1793292012), quote.QuotedAt);
    }

    [Fact]
    public async Task FetchAsync_ReadsFirstResultOnly()
    {
        _api.RespondWithBody("""
            {"chart":{"result":[{"meta":{"regularMarketPrice":22.61,"regularMarketTime":1793292012}},{"meta":{"regularMarketPrice":99,"regularMarketTime":1}}]}}
            """);

        var quote = await _source.FetchAsync(TestData.Petr4, Ct);

        Assert.Equal(22.61m, quote.Price);
    }

    [Fact]
    public async Task FetchAsync_RequestsChartForB3Symbol()
    {
        await _source.FetchAsync(Ticker.Create("bova11"), Ct);

        var request = _api.LastRequest;
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("https://yahoo.test/v8/finance/chart/BOVA11.SA?interval=1d&range=1d", request.Uri.ToString());
    }

    [Fact]
    public async Task FetchAsync_SendsUserAgent()
    {
        await _source.FetchAsync(TestData.Petr4, Ct);

        Assert.Equal(new YahooFinanceOptions().UserAgent, _api.LastRequest.UserAgent);
    }

    [Fact]
    public async Task FetchAsync_SendsConfiguredUserAgent()
    {
        var source = new YahooFinanceQuoteSource(_client, Options.Create(new YahooFinanceOptions { UserAgent = "custom-agent/2.0" }));

        await source.FetchAsync(TestData.Petr4, Ct);

        Assert.Equal("custom-agent/2.0", _api.LastRequest.UserAgent);
    }

    [Fact]
    public async Task FetchAsync_SendsUserAgentOnEveryRequest()
    {
        await _source.FetchAsync(TestData.Petr4, Ct);
        await _source.FetchAsync(TestData.Petr4, Ct);

        Assert.All(_api.Requests, request => Assert.Equal(new YahooFinanceOptions().UserAgent, request.UserAgent));
        Assert.Empty(_client.DefaultRequestHeaders.UserAgent);
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
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task FetchAsync_WhenStatusIsNotSuccess_ThrowsRecoverableException(HttpStatusCode statusCode)
    {
        _api.RespondWithStatus(statusCode);

        var exception = await Assert.ThrowsAsync<QuoteProviderException>(() => _source.FetchAsync(TestData.Petr4, Ct));

        Assert.True(exception.IsRecoverable);
        Assert.Equal($"Yahoo Finance responded with HTTP {(int)statusCode} for PETR4.", exception.Message);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("""{"chart":null}""")]
    [InlineData("""{"chart":{"result":null,"error":{"code":"Not Found"}}}""")]
    [InlineData("""{"chart":{"result":[]}}""")]
    [InlineData("""{"chart":{"result":[{"meta":null}]}}""")]
    [InlineData("""{"chart":{"result":[{"meta":{"regularMarketTime":1793292012}}]}}""")]
    [InlineData("""{"chart":{"result":[{"meta":{"regularMarketPrice":22.61}}]}}""")]
    [InlineData("""{"chart":{"result":[{"meta":{"regularMarketPrice":null,"regularMarketTime":1793292012}}]}}""")]
    public async Task FetchAsync_WhenPayloadHasNoPrice_ThrowsRecoverableException(string payload)
    {
        _api.RespondWithBody(payload);

        var exception = await Assert.ThrowsAsync<QuoteProviderException>(() => _source.FetchAsync(TestData.Petr4, Ct));

        Assert.IsNotType<TickerNotFoundException>(exception);
        Assert.True(exception.IsRecoverable);
        Assert.Equal("Yahoo Finance returned no price for PETR4.", exception.Message);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"chart":{"result":[{"meta":{"regularMarketPrice":"abc","regularMarketTime":1793292012}}]}}""")]
    [InlineData("""{"chart":""")]
    public async Task FetchAsync_WhenPayloadIsInvalidJson_ThrowsRecoverableException(string payload)
    {
        _api.RespondWithBody(payload);

        var exception = await Assert.ThrowsAsync<QuoteProviderException>(() => _source.FetchAsync(TestData.Petr4, Ct));

        Assert.True(exception.IsRecoverable);
        Assert.Equal("Yahoo Finance returned an invalid response for PETR4.", exception.Message);
        Assert.IsAssignableFrom<System.Text.Json.JsonException>(exception.InnerException);
    }

    [Theory]
    [InlineData(-62135596801)]
    [InlineData(253402300800)]
    public async Task FetchAsync_WhenQuoteTimeIsOutOfRange_ThrowsRecoverableException(long unixTime)
    {
        _api.RespondWithBody($$$"""{"chart":{"result":[{"meta":{"regularMarketPrice":22.61,"regularMarketTime":{{{unixTime}}}}}]}}""");

        var exception = await Assert.ThrowsAsync<QuoteProviderException>(() => _source.FetchAsync(TestData.Petr4, Ct));

        Assert.True(exception.IsRecoverable);
        Assert.Equal($"Yahoo Finance returned an invalid quote time ({unixTime}) for PETR4.", exception.Message);
    }

    [Fact]
    public async Task FetchAsync_WhenNetworkFails_ThrowsRecoverableExceptionWrappingTheError()
    {
        var networkError = new HttpRequestException("Connection refused");
        _api.FailWith(networkError);

        var exception = await Assert.ThrowsAsync<QuoteProviderException>(() => _source.FetchAsync(TestData.Petr4, Ct));

        Assert.True(exception.IsRecoverable);
        Assert.Same(networkError, exception.InnerException);
        Assert.Equal("Could not reach Yahoo Finance for PETR4: Connection refused", exception.Message);
    }

    [Fact]
    public async Task FetchAsync_WhenRequestTimesOut_ThrowsRecoverableException()
    {
        _api.FailWith(new TaskCanceledException("timeout", new TimeoutException()));

        var exception = await Assert.ThrowsAsync<QuoteProviderException>(() => _source.FetchAsync(TestData.Petr4, Ct));

        Assert.True(exception.IsRecoverable);
        Assert.Equal("Yahoo Finance did not answer in time for PETR4.", exception.Message);
        Assert.IsType<TaskCanceledException>(exception.InnerException);
    }

    public static TheoryData<Exception> RejectedByResiliencePipeline => new()
    {
        new TimeoutRejectedException("The operation didn't complete within the allowed timeout of '00:00:10'."),
        new BrokenCircuitException("The circuit is now open and is not allowing calls."),
    };

    [Theory]
    [MemberData(nameof(RejectedByResiliencePipeline))]
    public async Task FetchAsync_WhenResiliencePipelineRejects_ThrowsRecoverableException(Exception rejection)
    {
        _api.FailWith(rejection);

        var exception = await Assert.ThrowsAsync<QuoteProviderException>(() => _source.FetchAsync(TestData.Petr4, Ct));

        Assert.True(exception.IsRecoverable);
        Assert.Same(rejection, exception.InnerException);
        Assert.Equal($"Yahoo Finance is unavailable for PETR4: {rejection.Message}", exception.Message);
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

    [Fact]
    public async Task FetchAsync_AfterFailure_SucceedsOnNextCall()
    {
        _api.RespondWithStatus(HttpStatusCode.ServiceUnavailable).RespondWithPrices(22.70m);

        await Assert.ThrowsAsync<QuoteProviderException>(() => _source.FetchAsync(TestData.Petr4, Ct));
        var quote = await _source.FetchAsync(TestData.Petr4, Ct);

        Assert.Equal(22.70m, quote.Price);
        Assert.Equal(TestData.Now, quote.QuotedAt);
    }

    public void Dispose()
    {
        _client.Dispose();
        _api.Dispose();
    }
}