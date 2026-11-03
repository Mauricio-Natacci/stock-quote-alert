using StockQuoteAlert.Infrastructure.Quotes;
using StockQuoteAlert.Tests.TestDoubles;

namespace StockQuoteAlert.Tests.Infrastructure.Quotes;

public sealed class QuoteProviderOptionsTests
{
    [Fact]
    public void SectionName_IsQuoteProvider()
    {
        Assert.Equal("QuoteProvider", QuoteProviderOptions.SectionName);
    }

    [Fact]
    public void Defaults_UseYahooWithoutFallback()
    {
        var options = new QuoteProviderOptions();

        Assert.Equal(QuoteProviderKind.Yahoo, options.Provider);
        Assert.Null(options.Fallback);
        Assert.Empty(OptionsValidation.Errors(options));
    }

    [Theory]
    [InlineData(QuoteProviderKind.Yahoo, QuoteProviderKind.Brapi)]
    [InlineData(QuoteProviderKind.Brapi, QuoteProviderKind.Yahoo)]
    public void Validate_WithDifferentFallback_Passes(QuoteProviderKind provider, QuoteProviderKind fallback)
    {
        Assert.Empty(OptionsValidation.Errors(new QuoteProviderOptions { Provider = provider, Fallback = fallback }));
    }

    [Theory]
    [InlineData(QuoteProviderKind.Yahoo)]
    [InlineData(QuoteProviderKind.Brapi)]
    public void Validate_WithFallbackEqualToProvider_Fails(QuoteProviderKind kind)
    {
        var errors = OptionsValidation.Errors(new QuoteProviderOptions { Provider = kind, Fallback = kind });

        Assert.Equal("QuoteProvider:Fallback must be different from QuoteProvider:Provider.", Assert.Single(errors));
    }

    [Fact]
    public void Validate_WithBrapiAloneAndNoFallback_Passes()
    {
        Assert.Empty(OptionsValidation.Errors(new QuoteProviderOptions { Provider = QuoteProviderKind.Brapi }));
    }

    [Fact]
    public void Validate_WithUndefinedProvider_Fails()
    {
        var errors = OptionsValidation.Errors(new QuoteProviderOptions { Provider = (QuoteProviderKind)7 });

        Assert.Equal("QuoteProvider:Provider must be Yahoo or Brapi.", Assert.Single(errors));
    }

    [Fact]
    public void Validate_WithUndefinedFallback_Fails()
    {
        var errors = OptionsValidation.Errors(new QuoteProviderOptions { Fallback = (QuoteProviderKind)7 });

        Assert.Equal("QuoteProvider:Fallback must be Yahoo, Brapi or empty.", Assert.Single(errors));
    }
}