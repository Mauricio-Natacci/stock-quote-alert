using Microsoft.Extensions.Logging;
using StockQuoteAlert.Core.Application.Abstractions;
using StockQuoteAlert.Core.Application.Exceptions;
using StockQuoteAlert.Core.Domain;

namespace StockQuoteAlert.Infrastructure.Quotes;

/// <summary>
/// Decorates the primary provider: asks it first and the fallback only when it fails (even for an unknown ticker,
/// which the other provider may know). When both fail, the errors are combined and monitoring keeps retrying if
/// either failure is recoverable (e.g. Yahoo offline while brapi has no token).
/// </summary>
internal sealed partial class FallbackQuoteProvider(
    QuoteSourceAdapter primary,
    QuoteSourceAdapter fallback,
    ILogger<FallbackQuoteProvider> logger) : IQuoteProvider
{
    public async Task<Quote> GetQuoteAsync(Ticker ticker, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ticker);

        try
        {
            return await primary.GetQuoteAsync(ticker, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception primaryFailure) when (!cancellationToken.IsCancellationRequested)
        {
            LogFallingBack(primary.Name, ticker.Symbol, fallback.Name, primaryFailure.Message);

            try
            {
                return await fallback.GetQuoteAsync(ticker, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception fallbackFailure) when (!cancellationToken.IsCancellationRequested)
            {
                throw new QuoteProviderException(
                    $"All quote providers failed for {ticker}. {primary.Name}: {primaryFailure.Message} {fallback.Name}: {fallbackFailure.Message}",
                    new AggregateException(primaryFailure, fallbackFailure),
                    IsRecoverable(primaryFailure) || IsRecoverable(fallbackFailure));
            }
        }
    }

    private static bool IsRecoverable(Exception exception) =>
        exception is not QuoteProviderException { IsRecoverable: false };

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Primary} failed for {Ticker}, using {Fallback}: {Error}")]
    private partial void LogFallingBack(string primary, string ticker, string fallback, string error);
}