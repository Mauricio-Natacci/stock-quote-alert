namespace StockQuoteAlert.Core.Domain;

/// <param name="Source">Display name of the provider that served the quote (e.g. "Yahoo Finance").</param>
public sealed record Quote(Ticker Ticker, decimal Price, DateTimeOffset QuotedAt, string Source);