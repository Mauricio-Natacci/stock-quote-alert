using StockQuoteAlert.Core.Application.Exceptions;

namespace StockQuoteAlert.Tests.Core.Application.Exceptions;

public sealed class QuoteProviderExceptionTests
{
    [Fact]
    public void DefaultConstructor_IsRecoverable()
    {
        Assert.True(new QuoteProviderException().IsRecoverable);
    }

    [Fact]
    public void MessageConstructor_KeepsMessageAndIsRecoverable()
    {
        var exception = new QuoteProviderException("timeout");

        Assert.Equal("timeout", exception.Message);
        Assert.True(exception.IsRecoverable);
    }

    [Fact]
    public void WrappingConstructor_KeepsInnerExceptionAndIsRecoverable()
    {
        var inner = new HttpRequestException("network");

        var exception = new QuoteProviderException("timeout", inner);

        Assert.Equal("timeout", exception.Message);
        Assert.Same(inner, exception.InnerException);
        Assert.True(exception.IsRecoverable);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Constructor_WithRecoverableFlag_KeepsIt(bool isRecoverable)
    {
        var exception = new QuoteProviderException("rejected", isRecoverable);

        Assert.Equal("rejected", exception.Message);
        Assert.Equal(isRecoverable, exception.IsRecoverable);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void WrappingConstructor_WithRecoverableFlag_KeepsItAndInnerException(bool isRecoverable)
    {
        var inner = new HttpRequestException("network");

        var exception = new QuoteProviderException("rejected", inner, isRecoverable);

        Assert.Equal("rejected", exception.Message);
        Assert.Same(inner, exception.InnerException);
        Assert.Equal(isRecoverable, exception.IsRecoverable);
    }
}