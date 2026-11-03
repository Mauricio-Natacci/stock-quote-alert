using StockQuoteAlert.Core.Application.Exceptions;
using StockQuoteAlert.Core.Domain;

namespace StockQuoteAlert.Infrastructure.Quotes;

/// <summary>
/// Speaks a single external quote API and returns its data as-is, before any normalization.
/// </summary>
internal interface IQuoteSource
{
    /// <summary>Display name of the provider (e.g. "Yahoo Finance").</summary>
    string Name { get; }

    /// <exception cref="TickerNotFoundException">The provider does not know the ticker.</exception>
    /// <exception cref="QuoteProviderException">The quote could not be fetched or the response was not understood.</exception>
    Task<RawQuote> FetchAsync(Ticker ticker, CancellationToken cancellationToken);
}