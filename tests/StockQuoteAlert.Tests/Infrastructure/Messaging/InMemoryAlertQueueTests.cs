using StockQuoteAlert.Infrastructure.Messaging;
using StockQuoteAlert.Tests.TestDoubles;

namespace StockQuoteAlert.Tests.Infrastructure.Messaging;

public sealed class InMemoryAlertQueueTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Publish_EnqueuesWithoutWaitingForAReader()
    {
        var queue = new InMemoryAlertQueue();
        var sell = TestData.Raised(TestData.SellAlert());
        var buy = TestData.Raised(TestData.BuyAlert());

        await queue.PublishAsync(sell, Ct);
        await queue.PublishAsync(buy, Ct);

        Assert.True(queue.Reader.TryRead(out var first));
        Assert.True(queue.Reader.TryRead(out var second));
        Assert.Same(sell, first);
        Assert.Same(buy, second);
        Assert.False(queue.Reader.TryRead(out _));
    }

    [Fact]
    public async Task Publish_ManyAlertsNeverBlocks()
    {
        var queue = new InMemoryAlertQueue();

        for (var i = 0; i < 10_000; i++)
        {
            await queue.PublishAsync(TestData.Raised(TestData.SellAlert()), Ct);
        }

        var read = 0;
        while (queue.Reader.TryRead(out _))
        {
            read++;
        }

        Assert.Equal(10_000, read);
    }

    [Fact]
    public async Task Publish_WithNullAlert_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await new InMemoryAlertQueue().PublishAsync(null!, Ct));
    }
}