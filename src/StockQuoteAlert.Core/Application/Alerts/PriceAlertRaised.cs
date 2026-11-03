using StockQuoteAlert.Core.Domain;

namespace StockQuoteAlert.Core.Application.Alerts;

/// <summary>Message published when a stock enters an alert zone; every consumer receives the same instance.</summary>
public sealed record PriceAlertRaised(Guid Id, PriceAlert Alert, DateTimeOffset RaisedAt);