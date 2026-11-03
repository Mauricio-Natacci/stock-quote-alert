using Microsoft.Extensions.Logging;
using StockQuoteAlert.Core.Application.Abstractions;
using StockQuoteAlert.Core.Application.Alerts;
using StockQuoteAlert.Core.Domain;

namespace StockQuoteAlert.Infrastructure.Sms;

/// <summary>Example consumer showing where an SMS gateway plugs in. It only logs; no message is sent.</summary>
internal sealed partial class SmsAlertConsumer(ILogger<SmsAlertConsumer> logger) : IAlertConsumer
{
    public string Name => "SMS";

    public Task ConsumeAsync(PriceAlertRaised alert, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(alert);

        LogReceived(alert.Alert.Kind, alert.Alert.Ticker.Symbol, alert.Alert.CurrentPrice);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "{Kind} alert for {Ticker} at {Price:F2} received; sending SMS is not implemented (example consumer)")]
    private partial void LogReceived(AlertKind kind, string ticker, decimal price);
}