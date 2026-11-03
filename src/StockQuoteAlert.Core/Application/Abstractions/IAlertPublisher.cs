using StockQuoteAlert.Core.Application.Alerts;

namespace StockQuoteAlert.Core.Application.Abstractions;

/// <summary>Hands an alert over for delivery and returns without waiting for the consumers.</summary>
public interface IAlertPublisher
{
    ValueTask PublishAsync(PriceAlertRaised alert, CancellationToken cancellationToken);
}