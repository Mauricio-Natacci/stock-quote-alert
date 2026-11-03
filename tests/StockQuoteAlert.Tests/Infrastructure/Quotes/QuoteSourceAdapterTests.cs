using System.Net;
using Microsoft.Extensions.Options;
using StockQuoteAlert.Core.Application.Exceptions;
using StockQuoteAlert.Core.Domain;
using StockQuoteAlert.Infrastructure.Quotes;
using StockQuoteAlert.Infrastructure.Quotes.Yahoo;
using StockQuoteAlert.Tests.TestDoubles;

namespace StockQuoteAlert.Tests.Infrastructure.Quotes;

/// <summary>Runs the real Yahoo source through the adapter; only the HTTP API is faked.</summary>
public sealed class QuoteSourceAdapterTests : IDisposable
{
    private readonly FakeQuoteApi _api = new();
    private readonly HttpClient _client;
    private readonly QuoteSourceAdapter _adapter;

    public QuoteSourceAdapterTests()
    {
        _client = _api.CreateClient();
        _adapter = new QuoteSourceAdapter(new YahooFinanceQuoteSource(_client, Options.Create(new YahooFinanceOptions())));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task GetQuoteAsync_ReturnsDomainQuoteTaggedWithSourceName()
    {
        _api.RespondWithPrices(22.63m);

        var quote = await _adapter.GetQuoteAsync(TestData.Petr4, Ct);

        Assert.Equal(new Quote(TestData.Petr4, 22.63m, TestData.Now, "Yahoo Finance"), quote);
    }

    public static TheoryData<decimal, decimal> ReportedAndRoundedPrices => new()
    {
        { 22.6299991607666m, 22.63m },
        { 22.635m, 22.64m },
        { 22.625m, 22.63m },
        { 22.634m, 22.63m },
        { 22.6m, 22.60m },
        { 0.005m, 0.01m },
    };

    [Theory]
    [MemberData(nameof(ReportedAndRoundedPrices))]
    public async Task GetQuoteAsync_RoundsPriceToCentsHalfAwayFromZero(decimal reported, decimal expected)
    {
        _api.RespondWithPrices(reported);

        var quote = await _adapter.GetQuoteAsync(TestData.Petr4, Ct);

        Assert.Equal(expected, quote.Price);
    }

    [Fact]
    public async Task GetQuoteAsync_ReturnsTimeInUtc()
    {
        var quotedAt = new DateTimeOffset(2026, 10, 29, 13, 54, 30, TimeSpan.FromHours(-3));
        var adapter = new QuoteSourceAdapter(new FixedQuoteSource(new RawQuote(22.63m, quotedAt)));

        var quote = await adapter.GetQuoteAsync(TestData.Petr4, Ct);

        Assert.Equal(TimeSpan.Zero, quote.QuotedAt.Offset);
        Assert.Equal(new DateTimeOffset(2026, 10, 29, 16, 54, 30, TimeSpan.Zero), quote.QuotedAt);
    }

    public static TheoryData<decimal> NonPositivePrices => new() { 0m, -1m, -0.01m, 0.004m };

    [Theory]
    [MemberData(nameof(NonPositivePrices))]
    public async Task GetQuoteAsync_WithNonPositivePrice_ThrowsRecoverableException(decimal reported)
    {
        _api.RespondWithPrices(reported);

        var exception = await Assert.ThrowsAsync<QuoteProviderException>(() => _adapter.GetQuoteAsync(TestData.Petr4, Ct));

        Assert.True(exception.IsRecoverable);
        Assert.Equal($"Yahoo Finance returned an invalid price ({reported}) for PETR4.", exception.Message);
    }

    [Fact]
    public async Task GetQuoteAsync_WhenTickerIsUnknown_PassesTheSourceFailureThrough()
    {
        _api.RespondWithStatus(HttpStatusCode.NotFound);

        var exception = await Assert.ThrowsAsync<TickerNotFoundException>(() => _adapter.GetQuoteAsync(TestData.Petr4, Ct));

        Assert.Equal(TestData.Petr4, exception.Ticker);
    }

    [Fact]
    public async Task GetQuoteAsync_WhenSourceFails_PassesTheSourceFailureThrough()
    {
        _api.RespondWithStatus(HttpStatusCode.ServiceUnavailable);

        var exception = await Assert.ThrowsAsync<QuoteProviderException>(() => _adapter.GetQuoteAsync(TestData.Petr4, Ct));

        Assert.Equal("Yahoo Finance responded with HTTP 503 for PETR4.", exception.Message);
    }

    [Fact]
    public async Task GetQuoteAsync_WhenCancelled_PropagatesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _adapter.GetQuoteAsync(TestData.Petr4, cancellation.Token));
    }

    [Fact]
    public async Task GetQuoteAsync_WithNullTicker_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _adapter.GetQuoteAsync(null!, Ct));
        Assert.Empty(_api.Requests);
    }

    public void Dispose()
    {
        _client.Dispose();
        _api.Dispose();
    }

    /// <summary>Yahoo always reports UTC, so the offset conversion needs a source that reports local time.</summary>
    private sealed class FixedQuoteSource(RawQuote raw) : IQuoteSource
    {
        public string Name => "Fixed";

        public Task<RawQuote> FetchAsync(Ticker ticker, CancellationToken cancellationToken) => Task.FromResult(raw);
    }
}