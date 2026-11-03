using MimeKit;

namespace StockQuoteAlert.Tests.TestDoubles;

/// <param name="Body">The plain-text version.</param>
/// <param name="HtmlBody">The HTML version, when the message has one.</param>
/// <param name="IsPlainText">The whole message is a single plain-text part.</param>
/// <param name="IsAlternative">The message is multipart/alternative (text and HTML).</param>
/// <param name="Mime">The message bytes as handed to the server, to check header and body encoding.</param>
internal sealed record DeliveredEmail(
    string FromName,
    string FromAddress,
    string To,
    string Subject,
    string Body,
    string? HtmlBody,
    bool IsPlainText,
    bool IsAlternative,
    byte[] Mime)
{
    public static DeliveredEmail From(MimeMessage message)
    {
        var from = message.From.Mailboxes.Single();
        using var mime = new MemoryStream();
        message.WriteTo(mime);

        return new DeliveredEmail(
            from.Name ?? string.Empty,
            from.Address,
            message.To.Mailboxes.Single().Address,
            message.Subject ?? string.Empty,
            message.TextBody ?? string.Empty,
            message.HtmlBody,
            message.Body is TextPart { IsPlain: true },
            message.Body is MultipartAlternative,
            mime.ToArray());
    }
}