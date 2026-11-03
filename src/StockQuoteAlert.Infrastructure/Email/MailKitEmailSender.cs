using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace StockQuoteAlert.Infrastructure.Email;

/// <summary>
/// Sends each e-mail over its own SMTP connection: connect, authenticate when required, send, disconnect.
/// MailKit failures (connection, authentication, rejected message) propagate to the caller.
/// </summary>
internal sealed class MailKitEmailSender(
    ISmtpClientFactory clientFactory,
    IOptions<SmtpOptions> smtpOptions,
    IOptions<EmailOptions> emailOptions) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        var smtp = smtpOptions.Value;

        using var mimeMessage = BuildMimeMessage(message, emailOptions.Value);
        using var client = clientFactory.Create();

        client.Timeout = (int)smtp.Timeout.TotalMilliseconds;
        client.CheckCertificateRevocation = smtp.CheckCertificateRevocation;

        await client.ConnectAsync(smtp.Host, smtp.Port, MapSecurity(smtp.Security), cancellationToken).ConfigureAwait(false);

        if (smtp.RequireAuthentication)
        {
            await client.AuthenticateAsync(smtp.Username, smtp.Password, cancellationToken).ConfigureAwait(false);
        }

        await client.SendAsync(mimeMessage, cancellationToken).ConfigureAwait(false);
        await client.DisconnectAsync(quit: true, cancellationToken).ConfigureAwait(false);
    }

    internal static MimeMessage BuildMimeMessage(EmailMessage message, EmailOptions options)
    {
        // With an HTML body this is multipart/alternative (text first, HTML preferred); otherwise a single text part.
        var body = new BodyBuilder { TextBody = message.TextBody, HtmlBody = message.HtmlBody };
        var mimeMessage = new MimeMessage
        {
            Subject = message.Subject,
            Body = body.ToMessageBody(),
        };

        mimeMessage.From.Add(new MailboxAddress(options.SenderName, options.SenderAddress));
        mimeMessage.To.Add(MailboxAddress.Parse(message.To));

        return mimeMessage;
    }

    internal static SecureSocketOptions MapSecurity(SmtpSecurity security) => security switch
    {
        SmtpSecurity.StartTls => SecureSocketOptions.StartTls,
        SmtpSecurity.SslOnConnect => SecureSocketOptions.SslOnConnect,
        SmtpSecurity.None => SecureSocketOptions.None,
        _ => throw new ArgumentOutOfRangeException(nameof(security), security, "Unsupported SMTP security mode."),
    };
}