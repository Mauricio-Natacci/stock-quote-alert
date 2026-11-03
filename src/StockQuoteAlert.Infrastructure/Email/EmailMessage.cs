namespace StockQuoteAlert.Infrastructure.Email;

/// <param name="To">A single bare address such as "dest@gmail.com".</param>
internal sealed record EmailMessage(string To, string Subject, string TextBody);