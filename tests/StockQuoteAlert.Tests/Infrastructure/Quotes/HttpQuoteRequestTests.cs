using System.Net;
using StockQuoteAlert.Core.Application.Exceptions;
using StockQuoteAlert.Infrastructure.Quotes;
using StockQuoteAlert.Infrastructure.Quotes.Brapi;
using StockQuoteAlert.Tests.TestDoubles;

namespace StockQuoteAlert.Tests.Infrastructure.Quotes;

/// <summary>Covers the shared request rules directly; each source's tests cover them through the source too.</summary>
public sealed class HttpQuoteRequestTests : IDisposable
{
    private readonly FakeQuoteApi _api = FakeQuoteApi.Brapi();
    private readonly HttpClient _client;

    public HttpQuoteRequestTests()
    {
        _client = _api.CreateClient();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ReturnsTheDeserializedBody()
    {
        _api.RespondWithPrices(22.70m);

        var payload = await SendAsync(rejects: null);

        Assert.Equal(22.70m, Assert.Single(payload!.Results!).RegularMarketPrice);
    }

    [Fact]
    public async Task NullBody_ReturnsNull()
    {
        _api.RespondWithBody("null");

        Assert.Null(await SendAsync(rejects: null));
    }

    [Fact]
    public async Task RejectedStatus_ThrowsTheSourceException()
    {
        var rejection = new QuoteProviderException("rejected", isRecoverable: false);
        _api.RespondWithStatus(HttpStatusCode.Forbidden);

        var exception = await Assert.ThrowsAsync<QuoteProviderException>(() => SendAsync(status => status == HttpStatusCode.Forbidden ? rejection : null));

        Assert.Same(rejection, exception);
    }

    [Fact]
    public async Task StatusNotRejected_UsesTheDefaultRule()
    {
        _api.RespondWithStatus(HttpStatusCode.Forbidden);

        var exception = await Assert.ThrowsAsync<QuoteProviderException>(() => SendAsync(_ => null));

        Assert.True(exception.IsRecoverable);
        Assert.Equal("Source responded with HTTP 403 for PETR4.", exception.Message);
    }

    [Fact]
    public async Task NotFound_WinsOverRejection()
    {
        _api.RespondWithStatus(HttpStatusCode.NotFound);

        await Assert.ThrowsAsync<TickerNotFoundException>(() => SendAsync(_ => new QuoteProviderException("rejected", isRecoverable: false)));
    }

    [Fact]
    public async Task WithNullClientOrRequest_Throws()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/quote/PETR4");

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            HttpQuoteRequest.GetJsonAsync(null!, request, BrapiJsonContext.Default.BrapiQuoteResponse, "Source", TestData.Petr4, null, Ct));
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            HttpQuoteRequest.GetJsonAsync(_client, null!, BrapiJsonContext.Default.BrapiQuoteResponse, "Source", TestData.Petr4, null, Ct));
    }

    public void Dispose()
    {
        _client.Dispose();
        _api.Dispose();
    }

    private async Task<BrapiQuoteResponse?> SendAsync(Func<HttpStatusCode, QuoteProviderException?>? rejects)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("api/quote/PETR4", UriKind.Relative));

        return await HttpQuoteRequest.GetJsonAsync(_client, request, BrapiJsonContext.Default.BrapiQuoteResponse, "Source", TestData.Petr4, rejects, Ct);
    }
}