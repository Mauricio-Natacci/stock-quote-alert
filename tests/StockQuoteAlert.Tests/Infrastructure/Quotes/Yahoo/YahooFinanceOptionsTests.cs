using StockQuoteAlert.Infrastructure.Quotes.Yahoo;
using StockQuoteAlert.Tests.TestDoubles;

namespace StockQuoteAlert.Tests.Infrastructure.Quotes.Yahoo;

public sealed class YahooFinanceOptionsTests
{
    [Fact]
    public void SectionName_IsUnderQuoteProvider()
    {
        Assert.Equal("QuoteProvider:Yahoo", YahooFinanceOptions.SectionName);
    }

    [Fact]
    public void Defaults_PointToYahooWithBrowserLikeUserAgent()
    {
        var options = new YahooFinanceOptions();

        Assert.Equal(new Uri("https://query1.finance.yahoo.com/"), options.BaseUrl);
        Assert.Equal("Mozilla/5.0 (compatible; stock-quote-alert/1.0)", options.UserAgent);
        Assert.Empty(Validate(options));
    }

    [Theory]
    [InlineData("https://yahoo.test/")]
    [InlineData("http://localhost:8080/")]
    [InlineData("https://yahoo.test/api/")]
    public void Validate_WithValidBaseUrl_Passes(string baseUrl)
    {
        Assert.Empty(Validate(new YahooFinanceOptions { BaseUrl = new Uri(baseUrl, UriKind.RelativeOrAbsolute) }));
    }

    [Theory]
    [InlineData("finance/", "QuoteProvider:Yahoo:BaseUrl must be an absolute http or https URL.")]
    [InlineData("ftp://yahoo.test/", "QuoteProvider:Yahoo:BaseUrl must be an absolute http or https URL.")]
    [InlineData("https://yahoo.test/api", "QuoteProvider:Yahoo:BaseUrl must end with '/'.")]
    public void Validate_WithInvalidBaseUrl_Fails(string baseUrl, string expectedError)
    {
        var errors = Validate(new YahooFinanceOptions { BaseUrl = new Uri(baseUrl, UriKind.RelativeOrAbsolute) });

        Assert.Equal(expectedError, Assert.Single(errors));
    }

    [Fact]
    public void Validate_WithoutBaseUrl_Fails()
    {
        var errors = Validate(new YahooFinanceOptions { BaseUrl = null! });

        Assert.Equal("QuoteProvider:Yahoo:BaseUrl is required.", Assert.Single(errors));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Validate_WithoutUserAgent_Fails(string? userAgent)
    {
        var errors = Validate(new YahooFinanceOptions { UserAgent = userAgent! });

        Assert.Equal("QuoteProvider:Yahoo:UserAgent is required.", Assert.Single(errors));
    }

    [Theory]
    [InlineData("agent/1.0 (unclosed comment")]
    [InlineData("agent\u0001")]
    public void Validate_WithMalformedUserAgent_Fails(string userAgent)
    {
        var errors = Validate(new YahooFinanceOptions { UserAgent = userAgent });

        Assert.Equal("QuoteProvider:Yahoo:UserAgent is not a valid User-Agent header value.", Assert.Single(errors));
    }

    private static List<string?> Validate(YahooFinanceOptions options) => OptionsValidation.Errors(options);
}