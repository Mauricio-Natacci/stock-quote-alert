using MailKit.Net.Smtp;

namespace StockQuoteAlert.Infrastructure.Email;

/// <summary>Creates a new MailKit client for each e-mail, so tests can replace the SMTP server.</summary>
internal interface ISmtpClientFactory
{
    ISmtpClient Create();
}