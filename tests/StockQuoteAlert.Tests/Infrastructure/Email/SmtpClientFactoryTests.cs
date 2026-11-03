using MailKit.Net.Smtp;
using StockQuoteAlert.Infrastructure.Email;

namespace StockQuoteAlert.Tests.Infrastructure.Email;

public sealed class SmtpClientFactoryTests
{
    [Fact]
    public void Create_ReturnsNewMailKitClientEachTime()
    {
        var factory = new SmtpClientFactory();

        using var first = factory.Create();
        using var second = factory.Create();

        Assert.IsType<SmtpClient>(first);
        Assert.NotSame(first, second);
    }

    [Fact]
    public void Create_ReturnsDisconnectedClient()
    {
        using var client = new SmtpClientFactory().Create();

        Assert.False(client.IsConnected);
    }
}