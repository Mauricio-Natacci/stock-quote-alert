using StockQuoteAlert.Core.Application.Exceptions;
using StockQuoteAlert.Core.Domain;

namespace StockQuoteAlert.Core.Application.Abstractions;

public interface IQuoteProvider
{
    /// <summary>Gets the latest quote for a ticker.</summary>
    /// <exception cref="TickerNotFoundException">The provider does not know the ticker.</exception>
    /// <exception cref="QuoteProviderException">The quote could not be fetched; see <see cref="QuoteProviderException.IsRecoverable"/>.</exception>
    Task<Quote> GetQuoteAsync(Ticker ticker, CancellationToken cancellationToken);
}