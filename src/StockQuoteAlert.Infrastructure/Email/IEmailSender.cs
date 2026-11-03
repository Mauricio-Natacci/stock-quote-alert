namespace StockQuoteAlert.Infrastructure.Email;

internal interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}