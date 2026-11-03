namespace StockQuoteAlert.Infrastructure.Quotes.Yahoo;

internal sealed record YahooChart(IReadOnlyList<YahooChartResult>? Result);