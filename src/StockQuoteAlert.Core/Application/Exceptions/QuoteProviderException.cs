namespace StockQuoteAlert.Core.Application.Exceptions;

public class QuoteProviderException : Exception
{
    public QuoteProviderException()
    {
    }

    public QuoteProviderException(string message)
        : base(message)
    {
    }

    public QuoteProviderException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public QuoteProviderException(string message, bool isRecoverable)
        : base(message)
    {
        IsRecoverable = isRecoverable;
    }

    public QuoteProviderException(string message, Exception innerException, bool isRecoverable)
        : base(message, innerException)
    {
        IsRecoverable = isRecoverable;
    }

    /// <summary>
    /// <see langword="true"/> when trying again later may succeed (network errors, HTTP 5xx); <see langword="false"/> when
    /// retrying would repeat the same error (unknown ticker, rejected credentials). Defaults to <see langword="true"/>.
    /// </summary>
    public bool IsRecoverable { get; } = true;
}