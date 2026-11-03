using StockQuoteAlert.Infrastructure.Email;

namespace StockQuoteAlert.Tests.Infrastructure.Email;

public sealed class EmailContentTests
{
    [Fact]
    public void Constructor_ExposesSubjectAndTextBody()
    {
        var content = new EmailContent("subject", "body");

        Assert.Equal("subject", content.Subject);
        Assert.Equal("body", content.TextBody);
    }

    [Fact]
    public void Contents_WithSameValues_AreEqual()
    {
        Assert.Equal(new EmailContent("subject", "body"), new EmailContent("subject", "body"));
        Assert.NotEqual(new EmailContent("subject", "body"), new EmailContent("subject", "other"));
    }
}