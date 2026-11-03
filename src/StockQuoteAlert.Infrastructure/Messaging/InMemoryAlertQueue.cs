using System.Threading.Channels;
using StockQuoteAlert.Core.Application.Abstractions;
using StockQuoteAlert.Core.Application.Alerts;

namespace StockQuoteAlert.Infrastructure.Messaging;

/// <summary>
/// In-process stand-in for a message broker: publishing only enqueues, and <see cref="AlertDispatcher"/> delivers.
/// Alerts still waiting in the queue are lost if the process stops.
/// </summary>
internal sealed class InMemoryAlertQueue : IAlertPublisher
{
    private readonly Channel<PriceAlertRaised> _channel = Channel.CreateUnbounded<PriceAlertRaised>(new UnboundedChannelOptions
    {
        SingleReader = true,
    });

    public ChannelReader<PriceAlertRaised> Reader => _channel.Reader;

    public ValueTask PublishAsync(PriceAlertRaised alert, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(alert);

        return _channel.Writer.WriteAsync(alert, cancellationToken);
    }
}