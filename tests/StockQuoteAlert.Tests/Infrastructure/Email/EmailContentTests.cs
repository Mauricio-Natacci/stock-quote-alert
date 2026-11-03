using StockQuoteAlert.Infrastructure.Email;

namespace StockQuoteAlert.Tests.Infrastructure.Email;

public sealed class EmailContentTests
{
    [Fact]
    public void Constructor_ExposesSubjectAndBothBodies()
    {
        var content = new EmailContent("subject", "text", "<p>html</p>");

        Assert.Equal("subject", content.Subject);
        Assert.Equal("text", content.TextBody);
        Assert.Equal("<p>html</p>", content.HtmlBody);
    }

    [Fact]
    public void Contents_WithSameValues_AreEqual()
    {
        Assert.Equal(new EmailContent("s", "t", "h"), new EmailContent("s", "t", "h"));
        Assert.NotEqual(new EmailContent("s", "t", "h"), new EmailContent("s", "t", "other"));
    }
}