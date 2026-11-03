using StockQuoteAlert.Infrastructure.Email;

namespace StockQuoteAlert.Tests.Infrastructure.Email;

public sealed class EmailMessageTests
{
    [Fact]
    public void Constructor_ExposesValues()
    {
        var message = new EmailMessage("dest@gmail.com", "subject", "body");

        Assert.Equal("dest@gmail.com", message.To);
        Assert.Equal("subject", message.Subject);
        Assert.Equal("body", message.TextBody);
        Assert.Null(message.HtmlBody);
    }

    [Fact]
    public void Constructor_WithHtml_ExposesIt()
    {
        Assert.Equal("<p>html</p>", new EmailMessage("dest@gmail.com", "subject", "body", "<p>html</p>").HtmlBody);
    }

    [Fact]
    public void Messages_WithSameValues_AreEqual()
    {
        Assert.Equal(new EmailMessage("a@b.com", "s", "b"), new EmailMessage("a@b.com", "s", "b"));
        Assert.NotEqual(new EmailMessage("a@b.com", "s", "b"), new EmailMessage("c@d.com", "s", "b"));
    }
}