using Microsoft.Extensions.Logging;
using StockQuoteAlert.Core.Domain;
using StockQuoteAlert.Infrastructure.Sms;
using StockQuoteAlert.Tests.TestDoubles;

namespace StockQuoteAlert.Tests.Infrastructure.Sms;

public sealed class SmsAlertConsumerTests
{
    private readonly FakeLogger<SmsAlertConsumer> _log = new();
    private readonly SmsAlertConsumer _consumer;

    public SmsAlertConsumerTests()
    {
        _consumer = new SmsAlertConsumer(_log);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Name_IsSms()
    {
        Assert.Equal("SMS", _consumer.Name);
    }

    [Fact]
    public async Task Consume_OnlyLogsThatTheAlertWasReceived()
    {
        await _consumer.ConsumeAsync(TestData.Raised(TestData.SellAlert(22.70m)), Ct);

        var entry = Assert.Single(_log.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal("Sell alert for PETR4 at 22.70 received; sending SMS is not implemented (example consumer)", entry.Message);
        Assert.Null(entry.Exception);
    }

    [Fact]
    public async Task Consume_LogIsStructured()
    {
        await _consumer.ConsumeAsync(TestData.Raised(TestData.BuyAlert(22.50m)), Ct);

        var entry = Assert.Single(_log.Entries);
        Assert.Equal(AlertKind.Buy, entry.Properties["Kind"]);
        Assert.Equal("PETR4", entry.Properties["Ticker"]);
        Assert.Equal(22.50m, entry.Properties["Price"]);
    }

    [Fact]
    public void Consume_CompletesRightAway()
    {
        Assert.True(_consumer.ConsumeAsync(TestData.Raised(TestData.SellAlert()), Ct).IsCompletedSuccessfully);
    }

    [Fact]
    public async Task Consume_WithNullAlert_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _consumer.ConsumeAsync(null!, Ct));

        Assert.Empty(_log.Entries);
    }
}