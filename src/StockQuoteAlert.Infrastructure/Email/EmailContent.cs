namespace StockQuoteAlert.Infrastructure.Email;

/// <param name="TextBody">Plain-text version, for clients that do not show HTML.</param>
internal sealed record EmailContent(string Subject, string TextBody, string HtmlBody);