using StockQuoteAlert.Core.Domain;

namespace StockQuoteAlert.Core.Application.Abstractions;

/// <summary>Tells the user about a price alert through one channel (e.g. e-mail).</summary>
public interface IAlertNotifier
{
    /// <summary>
    /// Delivers the alert. Delivery failures are reported by returning <see langword="false"/>, not by throwing;
    /// cancellation of <paramref name="cancellationToken"/> still throws <see cref="OperationCanceledException"/>.
    /// </summary>
    /// <returns><see langword="true"/> when the alert was delivered.</returns>
    Task<bool> NotifyAsync(PriceAlert alert, CancellationToken cancellationToken);
}