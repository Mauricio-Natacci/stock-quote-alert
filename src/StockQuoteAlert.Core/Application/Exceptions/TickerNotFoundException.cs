using StockQuoteAlert.Core.Domain;

namespace StockQuoteAlert.Core.Application.Exceptions;

/// <summary>The quote provider does not know the ticker. Never recoverable.</summary>
public sealed class TickerNotFoundException : QuoteProviderException
{
    public TickerNotFoundException(Ticker ticker)
        : base($"Ticker '{ticker}' was not found by the quote provider.", isRecoverable: false)
    {
        Ticker = ticker;
    }

    public Ticker Ticker { get; }
}