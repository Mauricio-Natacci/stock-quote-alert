using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StockQuoteAlert.Core.Application.Exceptions;
using StockQuoteAlert.Core.Domain;
using StockQuoteAlert.Infrastructure.Quotes;
using StockQuoteAlert.Infrastructure.Quotes.Brapi;
using StockQuoteAlert.Infrastructure.Quotes.Yahoo;
using StockQuoteAlert.Tests.TestDoubles;

namespace StockQuoteAlert.Tests.Infrastructure.Quotes;

/// <summary>Yahoo as primary and brapi as fallback, both real sources; only their HTTP APIs are faked.</summary>
public sealed class FallbackQuoteProviderTests : IDisposable
{
    private readonly FakeQuoteApi _yahoo = new();
    private readonly FakeQuoteApi _brapi = FakeQuoteApi.Brapi();
    private readonly HttpClient _yahooClient;
    private readonly HttpClient _brapiClient;
    private readonly FakeLogger<FallbackQuoteProvider> _log = new();
    private readonly FallbackQuoteProvider _provider;

    public FallbackQuoteProviderTests()
    {
        _yahooClient = _yahoo.CreateClient();
        _brapiClient = _brapi.CreateClient();
        _provider = new FallbackQuoteProvider(
            new QuoteSourceAdapter(new YahooFinanceQuoteSource(_yahooClient, Options.Create(new YahooFinanceOptions()))),
            new QuoteSourceAdapter(new BrapiQuoteSource(_brapiClient)),
            _log);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task PrimaryAnswers_FallbackIsNotAsked()
    {
        _yahoo.RespondWithPrices(22.70m);

        var quote = await _provider.GetQuoteAsync(TestData.Petr4, Ct);

        Assert.Equal(new Quote(TestData.Petr4, 22.70m, TestData.Now, "Yahoo Finance"), quote);
        Assert.Empty(_brapi.Requests);
        Assert.Empty(_log.Entries);
    }

    [Fact]
    public async Task PrimaryFails_QuoteComesFromFallback()
    {
        _yahoo.RespondWithStatus(HttpStatusCode.ServiceUnavailable);
        _brapi.RespondWithPrices(22.71m);

        var quote = await _provider.GetQuoteAsync(TestData.Petr4, Ct);

        Assert.Equal(new Quote(TestData.Petr4, 22.71m, TestData.Now, "brapi.dev"), quote);
        Assert.Single(_yahoo.Requests);
        Assert.Single(_brapi.Requests);
    }

    [Fact]
    public async Task PrimaryFails_LogsAStructuredWarning()
    {
        _yahoo.RespondWithStatus(HttpStatusCode.ServiceUnavailable);

        await _provider.GetQuoteAsync(TestData.Petr4, Ct);

        var warning = Assert.Single(_log.Entries);
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.Equal("Yahoo Finance failed for PETR4, using brapi.dev: Yahoo Finance responded with HTTP 503 for PETR4.", warning.Message);
        Assert.Equal("Yahoo Finance", warning.Properties["Primary"]);
        Assert.Equal("PETR4", warning.Properties["Ticker"]);
        Assert.Equal("brapi.dev", warning.Properties["Fallback"]);
    }

    [Fact]
    public async Task UnknownTickerOnPrimary_IsStillAskedToTheFallback()
    {
        _yahoo.RespondWithStatus(HttpStatusCode.NotFound);

        var quote = await _provider.GetQuoteAsync(TestData.Petr4, Ct);

        Assert.Equal("brapi.dev", quote.Source);
    }

    [Fact]
    public async Task InvalidPriceFromPrimary_FallsBack()
    {
        _yahoo.RespondWithPrices(0m);
        _brapi.RespondWithPrices(22.70m);

        Assert.Equal(22.70m, (await _provider.GetQuoteAsync(TestData.Petr4, Ct)).Price);
    }

    [Fact]
    public async Task BothFail_CombinesBothErrors()
    {
        _yahoo.RespondWithStatus(HttpStatusCode.ServiceUnavailable);
        _brapi.RespondWithStatus(HttpStatusCode.Unauthorized);

        var exception = await Assert.ThrowsAsync<QuoteProviderException>(() => _provider.GetQuoteAsync(TestData.Petr4, Ct));

        Assert.Equal(
            "All quote providers failed for PETR4. Yahoo Finance: Yahoo Finance responded with HTTP 503 for PETR4. " +
            "brapi.dev: brapi.dev rejected the request for PETR4 (HTTP 401). Check QuoteProvider:Brapi:Token.",
            exception.Message);
        var inner = Assert.IsType<AggregateException>(exception.InnerException);
        Assert.Collection(
            inner.InnerExceptions,
            primary => Assert.Equal("Yahoo Finance responded with HTTP 503 for PETR4.", primary.Message),
            fallback => Assert.StartsWith("brapi.dev rejected", fallback.Message, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, HttpStatusCode.ServiceUnavailable, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, HttpStatusCode.Unauthorized, true)]
    [InlineData(HttpStatusCode.NotFound, HttpStatusCode.ServiceUnavailable, true)]
    [InlineData(HttpStatusCode.NotFound, HttpStatusCode.Unauthorized, false)]
    [InlineData(HttpStatusCode.NotFound, HttpStatusCode.NotFound, false)]
    public async Task BothFail_StayRecoverableIfEitherFailureIs(HttpStatusCode yahooStatus, HttpStatusCode brapiStatus, bool recoverable)
    {
        _yahoo.RespondWithStatus(yahooStatus);
        _brapi.RespondWithStatus(brapiStatus);

        var exception = await Assert.ThrowsAsync<QuoteProviderException>(() => _provider.GetQuoteAsync(TestData.Petr4, Ct));

        Assert.Equal(recoverable, exception.IsRecoverable);
        Assert.IsNotType<TickerNotFoundException>(exception);
    }

    [Fact]
    public async Task UnexpectedExceptionFromASource_CountsAsRecoverable()
    {
        _yahoo.RespondWithStatus(HttpStatusCode.NotFound);
        _brapi.FailWith(new InvalidOperationException("boom"));

        var exception = await Assert.ThrowsAsync<QuoteProviderException>(() => _provider.GetQuoteAsync(TestData.Petr4, Ct));

        Assert.True(exception.IsRecoverable);
    }

    [Fact]
    public async Task Cancelled_PropagatesWithoutAskingTheFallback()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _provider.GetQuoteAsync(TestData.Petr4, cancellation.Token));

        Assert.Empty(_brapi.Requests);
        Assert.Empty(_log.Entries);
    }

    [Fact]
    public async Task CancelledWhileAskingTheFallback_Propagates()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        _yahoo.RespondWithStatus(HttpStatusCode.ServiceUnavailable);
        _brapi.RespondSlowly(TimeSpan.FromMinutes(5));

        var request = _provider.GetQuoteAsync(TestData.Petr4, cancellation.Token);
        Assert.True(await _brapi.RequestSignal.WaitAsync(TimeSpan.FromSeconds(5), Ct));
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
    }

    [Fact]
    public async Task WithNullTicker_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _provider.GetQuoteAsync(null!, Ct));
        Assert.Empty(_yahoo.Requests);
    }

    public void Dispose()
    {
        _yahooClient.Dispose();
        _brapiClient.Dispose();
        _yahoo.Dispose();
        _brapi.Dispose();
    }
}