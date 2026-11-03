namespace StockQuoteAlert.Infrastructure.Email;

/// <param name="To">A single bare address such as "dest@gmail.com".</param>
/// <param name="HtmlBody">When given, the e-mail is sent as multipart/alternative with both versions.</param>
internal sealed record EmailMessage(string To, string Subject, string TextBody, string? HtmlBody = null);