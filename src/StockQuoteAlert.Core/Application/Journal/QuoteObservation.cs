using StockQuoteAlert.Core.Domain;

namespace StockQuoteAlert.Core.Application.Journal;

/// <param name="ObservedAt">When the app received the quote (the quote itself carries the provider's time).</param>
public sealed record QuoteObservation(Quote Quote, PriceZone Zone, DateTimeOffset ObservedAt);