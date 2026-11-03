namespace StockQuoteAlert.Infrastructure.Quotes.Brapi;

internal sealed record BrapiQuoteResponse(IReadOnlyList<BrapiQuoteResult>? Results);