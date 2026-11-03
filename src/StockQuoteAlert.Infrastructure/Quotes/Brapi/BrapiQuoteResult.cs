namespace StockQuoteAlert.Infrastructure.Quotes.Brapi;

internal sealed record BrapiQuoteResult(string? Symbol, decimal? RegularMarketPrice, DateTimeOffset? RegularMarketTime);