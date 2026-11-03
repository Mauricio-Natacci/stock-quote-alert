using Microsoft.Extensions.Options;
using StockQuoteAlert.Infrastructure.Quotes.Brapi;
using StockQuoteAlert.Tests.TestDoubles;

namespace StockQuoteAlert.Tests.Infrastructure.Quotes.Brapi;

public sealed class BrapiAuthenticationHandlerTests : IDisposable
{
    private readonly FakeQuoteApi _api = FakeQuoteApi.Brapi();
    private readonly List<BrapiAuthenticationHandler> _handlers = [];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task WithToken_SendsItAsBearer()
    {
        await SendAsync(token: "abc123");

        Assert.Equal("Bearer abc123", _api.LastRequest.Authorization);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task WithoutToken_SendsNoAuthorization(string? token)
    {
        await SendAsync(token);

        Assert.Null(_api.LastRequest.Authorization);
    }

    [Fact]
    public async Task SendsTheTokenOnEveryRequest()
    {
        await SendAsync(token: "abc123", requests: 2);

        Assert.All(_api.Requests, request => Assert.Equal("Bearer abc123", request.Authorization));
    }

    [Fact]
    public async Task KeepsTheRestOfTheRequest()
    {
        await SendAsync(token: "abc123");

        Assert.Equal("https://brapi.test/api/quote/PETR4", _api.LastRequest.Uri.ToString());
        Assert.Equal(HttpMethod.Get, _api.LastRequest.Method);
    }

    // Each handler owns the fake as its inner handler, so disposing them also disposes the fake.
    public void Dispose() => _handlers.ForEach(handler => handler.Dispose());

    private async Task SendAsync(string? token, int requests = 1)
    {
        var handler = new BrapiAuthenticationHandler(Options.Create(new BrapiOptions { Token = token })) { InnerHandler = _api };
        _handlers.Add(handler);
        using var client = new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri("https://brapi.test/") };

        for (var i = 0; i < requests; i++)
        {
            using var response = await client.GetAsync(new Uri("api/quote/PETR4", UriKind.Relative), Ct);
        }
    }
}