using StockQuoteAlert.Infrastructure.Quotes.Brapi;
using StockQuoteAlert.Tests.TestDoubles;

namespace StockQuoteAlert.Tests.Infrastructure.Quotes.Brapi;

public sealed class BrapiOptionsTests
{
    [Fact]
    public void SectionName_IsUnderQuoteProvider()
    {
        Assert.Equal("QuoteProvider:Brapi", BrapiOptions.SectionName);
    }

    [Fact]
    public void Defaults_PointToBrapiWithoutToken()
    {
        var options = new BrapiOptions();

        Assert.Equal(new Uri("https://brapi.dev/"), options.BaseUrl);
        Assert.Null(options.Token);
        Assert.Empty(OptionsValidation.Errors(options));
    }

    [Theory]
    [InlineData("finance/", "QuoteProvider:Brapi:BaseUrl must be an absolute http or https URL.")]
    [InlineData("ftp://brapi.test/", "QuoteProvider:Brapi:BaseUrl must be an absolute http or https URL.")]
    [InlineData("https://brapi.test/api", "QuoteProvider:Brapi:BaseUrl must end with '/'.")]
    public void Validate_WithInvalidBaseUrl_Fails(string baseUrl, string expectedError)
    {
        var errors = OptionsValidation.Errors(new BrapiOptions { BaseUrl = new Uri(baseUrl, UriKind.RelativeOrAbsolute) });

        Assert.Equal(expectedError, Assert.Single(errors));
    }

    [Fact]
    public void Validate_WithoutBaseUrl_Fails()
    {
        Assert.Equal("QuoteProvider:Brapi:BaseUrl is required.", Assert.Single(OptionsValidation.Errors(new BrapiOptions { BaseUrl = null! })));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc123XYZ-_.")]
    public void Validate_WithEmptyOrPlainToken_Passes(string? token)
    {
        Assert.Empty(OptionsValidation.Errors(new BrapiOptions { Token = token }));
    }

    [Theory]
    [InlineData("abc 123")]
    [InlineData(" abc")]
    [InlineData("abc\r\nX-Injected: 1")]
    [InlineData("abc\t")]
    public void Validate_WithTokenContainingSpacesOrLineBreaks_Fails(string token)
    {
        var errors = OptionsValidation.Errors(new BrapiOptions { Token = token });

        Assert.Equal("QuoteProvider:Brapi:Token must not contain spaces or line breaks.", Assert.Single(errors));
    }
}