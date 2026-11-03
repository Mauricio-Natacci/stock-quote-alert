namespace StockQuoteAlert.Infrastructure.Quotes;

/// <summary>A quote exactly as the provider reported it, before normalization.</summary>
internal sealed record RawQuote(decimal Price, DateTimeOffset QuotedAt);