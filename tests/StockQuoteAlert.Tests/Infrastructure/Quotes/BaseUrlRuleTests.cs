using StockQuoteAlert.Infrastructure.Quotes;

namespace StockQuoteAlert.Tests.Infrastructure.Quotes;

public sealed class BaseUrlRuleTests
{
    [Theory]
    [InlineData("https://host.test/")]
    [InlineData("http://localhost:8080/")]
    [InlineData("https://host.test/api/")]
    public void Validate_WithAbsoluteHttpUrlEndingInSlash_Passes(string url)
    {
        Assert.Null(BaseUrlRule.Validate(new Uri(url), "Section:BaseUrl"));
    }

    [Fact]
    public void Validate_WithoutUrl_LeavesItToRequired()
    {
        Assert.Null(BaseUrlRule.Validate(null, "Section:BaseUrl"));
    }

    [Theory]
    [InlineData("relative/", "Section:BaseUrl must be an absolute http or https URL.")]
    [InlineData("ftp://host.test/", "Section:BaseUrl must be an absolute http or https URL.")]
    [InlineData("https://host.test/api", "Section:BaseUrl must end with '/'.")]
    public void Validate_WithInvalidUrl_NamesTheSetting(string url, string expected)
    {
        var result = BaseUrlRule.Validate(new Uri(url, UriKind.RelativeOrAbsolute), "Section:BaseUrl");

        Assert.Equal(expected, result?.ErrorMessage);
        Assert.Equal(["BaseUrl"], result!.MemberNames);
    }
}