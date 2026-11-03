using MailKit.Net.Smtp;

namespace StockQuoteAlert.Infrastructure.Email;

internal sealed class SmtpClientFactory : ISmtpClientFactory
{
    public ISmtpClient Create() => new SmtpClient();
}