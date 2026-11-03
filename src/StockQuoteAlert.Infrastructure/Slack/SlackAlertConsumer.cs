using Microsoft.Extensions.Logging;
using StockQuoteAlert.Core.Application.Abstractions;
using StockQuoteAlert.Core.Application.Alerts;
using StockQuoteAlert.Core.Domain;

namespace StockQuoteAlert.Infrastructure.Slack;

/// <summary>Example consumer showing where a Slack integration plugs in. It only logs; nothing is posted.</summary>
internal sealed partial class SlackAlertConsumer(ILogger<SlackAlertConsumer> logger) : IAlertConsumer
{
    public string Name => "Slack";

    public Task ConsumeAsync(PriceAlertRaised alert, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(alert);

        LogReceived(alert.Alert.Kind, alert.Alert.Ticker.Symbol, alert.Alert.CurrentPrice);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "{Kind} alert for {Ticker} at {Price:F2} received; posting to Slack is not implemented (example consumer)")]
    private partial void LogReceived(AlertKind kind, string ticker, decimal price);
}