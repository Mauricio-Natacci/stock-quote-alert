using StockQuoteAlert.Infrastructure.Quotes.Brapi;
using StockQuoteAlert.Infrastructure.Quotes.Yahoo;

namespace StockQuoteAlert.Infrastructure.Quotes;

public static class QuoteProviderKindExtensions
{
    /// <summary>The name each provider's quotes are tagged with (e.g. "Yahoo Finance").</summary>
    public static string DisplayName(this QuoteProviderKind kind) => kind switch
    {
        QuoteProviderKind.Yahoo => YahooFinanceQuoteSource.DisplayName,
        QuoteProviderKind.Brapi => BrapiQuoteSource.DisplayName,
        _ => kind.ToString(),
    };
}