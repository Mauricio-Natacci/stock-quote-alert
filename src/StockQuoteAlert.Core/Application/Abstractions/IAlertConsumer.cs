using StockQuoteAlert.Core.Application.Alerts;

namespace StockQuoteAlert.Core.Application.Abstractions;

/// <summary>Reacts to every published alert (e.g. by sending an e-mail). Throwing marks the delivery as failed, so it is retried.</summary>
public interface IAlertConsumer
{
    /// <summary>Channel name used in logs (e.g. "E-mail").</summary>
    string Name { get; }

    Task ConsumeAsync(PriceAlertRaised alert, CancellationToken cancellationToken);
}