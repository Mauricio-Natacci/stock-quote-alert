using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using NSubstitute;
using StockQuoteAlert.Infrastructure.Email;
using StockQuoteAlert.Tests.TestDoubles;
using Received = NSubstitute.Received;

namespace StockQuoteAlert.Tests.Infrastructure.Email;

public sealed class MailKitEmailSenderTests
{
    private static readonly EmailMessage Message = new("dest@gmail.com", "Subject", "Body");

    private readonly FakeSmtpServer _smtp = new();
    private readonly SmtpOptions _smtpOptions = TestData.SmtpOptions;
    private readonly EmailOptions _emailOptions = TestData.EmailOptions;
    private readonly MailKitEmailSender _sender;

    public MailKitEmailSenderTests()
    {
        _sender = new MailKitEmailSender(_smtp, Options.Create(_smtpOptions), Options.Create(_emailOptions));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SendAsync_ConnectsAuthenticatesSendsAndDisconnectsInOrder()
    {
        await _sender.SendAsync(Message, Ct);

        var client = _smtp.LastClient;
        Received.InOrder(() =>
        {
            client.ConnectAsync("smtp.gmail.com", 587, SecureSocketOptions.StartTls, Ct);
            client.AuthenticateAsync("me@gmail.com", "app-password", Ct);
            client.SendAsync(Arg.Any<MimeMessage>(), Ct, Arg.Any<ITransferProgress>());
            client.DisconnectAsync(true, Ct);
            client.Dispose();
        });
    }

    [Fact]
    public async Task SendAsync_UsesConfiguredServerAndCredentials()
    {
        _smtpOptions.Host = "smtp.example.com";
        _smtpOptions.Port = 465;
        _smtpOptions.Security = SmtpSecurity.SslOnConnect;
        _smtpOptions.Username = "user";
        _smtpOptions.Password = "secret";

        await _sender.SendAsync(Message, Ct);

        Assert.Equal(new SmtpConnection("smtp.example.com", 465, SecureSocketOptions.SslOnConnect), Assert.Single(_smtp.Connections));
        Assert.Equal(new SmtpLogin("user", "secret"), Assert.Single(_smtp.Logins));
    }

    [Fact]
    public async Task SendAsync_AppliesConfiguredTimeout()
    {
        _smtpOptions.Timeout = TimeSpan.FromSeconds(15);

        await _sender.SendAsync(Message, Ct);

        Assert.Equal(15_000, _smtp.LastClient.Timeout);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SendAsync_AppliesConfiguredRevocationCheck(bool check)
    {
        _smtpOptions.CheckCertificateRevocation = check;

        await _sender.SendAsync(Message, Ct);

        Assert.Equal(check, _smtp.LastClient.CheckCertificateRevocation);
    }

    [Fact]
    public async Task SendAsync_WithoutAuthentication_SkipsLogin()
    {
        _smtpOptions.RequireAuthentication = false;
        _smtpOptions.Security = SmtpSecurity.None;

        await _sender.SendAsync(Message, Ct);

        Assert.Empty(_smtp.Logins);
        Assert.Equal(SecureSocketOptions.None, Assert.Single(_smtp.Connections).Security);
        Assert.Single(_smtp.Delivered);
    }

    [Fact]
    public async Task SendAsync_DeliversPlainTextMessageFromConfiguredSenderToMessageRecipient()
    {
        await _sender.SendAsync(new EmailMessage("other@example.com", "Subject", "Body"), Ct);

        var delivered = Assert.Single(_smtp.Delivered);
        Assert.Equal("Stock Quote Alert", delivered.FromName);
        Assert.Equal("me@gmail.com", delivered.FromAddress);
        Assert.Equal("other@example.com", delivered.To);
        Assert.Equal("Subject", delivered.Subject);
        Assert.Equal("Body", delivered.Body);
        Assert.True(delivered.IsPlainText);
    }

    [Fact]
    public async Task SendAsync_WithHtml_DeliversTextAndHtmlAsAlternatives()
    {
        await _sender.SendAsync(new EmailMessage("dest@gmail.com", "Subject", "Text", "<p>Html</p>"), Ct);

        var delivered = Assert.Single(_smtp.Delivered);
        Assert.True(delivered.IsAlternative);
        Assert.False(delivered.IsPlainText);
        Assert.Equal("Text", delivered.Body);
        Assert.Equal("<p>Html</p>", delivered.HtmlBody);
    }

    [Fact]
    public async Task SendAsync_WithoutHtml_DeliversASingleTextPart()
    {
        await _sender.SendAsync(Message, Ct);

        var delivered = Assert.Single(_smtp.Delivered);
        Assert.True(delivered.IsPlainText);
        Assert.False(delivered.IsAlternative);
        Assert.Null(delivered.HtmlBody);
    }

    [Fact]
    public void BuildMimeMessage_WithHtml_PutsTextFirstAndHtmlLast()
    {
        using var message = MailKitEmailSender.BuildMimeMessage(new EmailMessage("dest@gmail.com", "Subject", "Text", "<p>Olá</p>"), _emailOptions);

        var alternative = Assert.IsType<MultipartAlternative>(message.Body);
        Assert.Collection(
            alternative,
            part => Assert.True(Assert.IsType<TextPart>(part).IsPlain),
            part => Assert.True(Assert.IsType<TextPart>(part).IsHtml));
        Assert.Equal("<p>Olá</p>", message.HtmlBody);
    }

    [Fact]
    public async Task SendAsync_KeepsPortugueseHtmlIntactOverTheWire()
    {
        await _sender.SendAsync(new EmailMessage("dest@gmail.com", "Subject", "Texto", "<p>Cotação: R$ 22,70</p>"), Ct);

        using var stream = new MemoryStream(Assert.Single(_smtp.Delivered).Mime);
        using var parsed = await MimeMessage.LoadAsync(stream, Ct);
        Assert.Equal("<p>Cotação: R$ 22,70</p>", parsed.HtmlBody?.TrimEnd());
    }

    [Fact]
    public async Task SendAsync_KeepsPortugueseTextIntactOverTheWire()
    {
        var message = new EmailMessage("dest@gmail.com", "[VENDA] PETR4 subiu para R$ 22,70", "Cotação atual: R$ 22,70\nReferência de venda: R$ 22,67");

        await _sender.SendAsync(message, Ct);

        using var stream = new MemoryStream(Assert.Single(_smtp.Delivered).Mime);
        using var parsed = await MimeMessage.LoadAsync(stream, Ct);
        var body = Assert.IsType<TextPart>(parsed.Body);
        Assert.Equal("utf-8", body.ContentType.Charset, ignoreCase: true);
        Assert.Equal(message.Subject, parsed.Subject);
        // Writing a MIME part always ends it with a line break.
        Assert.Equal(message.TextBody.ReplaceLineEndings(), body.Text.ReplaceLineEndings().TrimEnd());
    }

    [Fact]
    public async Task SendAsync_UsesANewClientForEveryMessage()
    {
        await _sender.SendAsync(Message, Ct);
        await _sender.SendAsync(Message, Ct);

        Assert.Equal(2, _smtp.Clients.Count);
        Assert.Equal(2, _smtp.Delivered.Count);
        Assert.All(_smtp.Clients, client => client.Received(1).Dispose());
    }

    [Fact]
    public async Task SendAsync_WhenConnectionFails_PropagatesAndDisposesClient()
    {
        var failure = new SmtpProtocolException("connection refused");
        _smtp.ConnectionFailure = failure;

        var exception = await Assert.ThrowsAsync<SmtpProtocolException>(() => _sender.SendAsync(Message, Ct));

        Assert.Same(failure, exception);
        Assert.Empty(_smtp.Logins);
        Assert.Empty(_smtp.Delivered);
        _smtp.LastClient.Received(1).Dispose();
    }

    [Fact]
    public async Task SendAsync_WhenAuthenticationFails_PropagatesWithoutSending()
    {
        _smtp.AuthenticationFailure = new AuthenticationException("535 bad credentials");

        await Assert.ThrowsAsync<AuthenticationException>(() => _sender.SendAsync(Message, Ct));

        Assert.Empty(_smtp.Delivered);
        _smtp.LastClient.Received(1).Dispose();
    }

    [Fact]
    public async Task SendAsync_WhenServerRejectsMessage_PropagatesAndDisposesClient()
    {
        _smtp.SendFailure = new SmtpCommandException(SmtpErrorCode.RecipientNotAccepted, SmtpStatusCode.MailboxUnavailable, "550 no such user");

        await Assert.ThrowsAsync<SmtpCommandException>(() => _sender.SendAsync(Message, Ct));

        await _smtp.LastClient.DidNotReceive().DisconnectAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>());
        _smtp.LastClient.Received(1).Dispose();
    }

    [Fact]
    public async Task SendAsync_WithNullMessage_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _sender.SendAsync(null!, Ct));
        Assert.Empty(_smtp.Clients);
    }

    [Fact]
    public void BuildMimeMessage_SetsSenderRecipientSubjectAndPlainTextBody()
    {
        using var message = MailKitEmailSender.BuildMimeMessage(Message, _emailOptions);

        var from = Assert.Single(message.From.Mailboxes);
        Assert.Equal("Stock Quote Alert", from.Name);
        Assert.Equal("me@gmail.com", from.Address);
        Assert.Equal("dest@gmail.com", Assert.Single(message.To.Mailboxes).Address);
        Assert.Equal("Subject", message.Subject);
        var body = Assert.IsType<TextPart>(message.Body);
        Assert.True(body.IsPlain);
        Assert.Equal("Body", body.Text);
    }

    [Theory]
    [InlineData(SmtpSecurity.StartTls, SecureSocketOptions.StartTls)]
    [InlineData(SmtpSecurity.SslOnConnect, SecureSocketOptions.SslOnConnect)]
    [InlineData(SmtpSecurity.None, SecureSocketOptions.None)]
    public void MapSecurity_MapsToMailKitOption(SmtpSecurity security, SecureSocketOptions expected)
    {
        Assert.Equal(expected, MailKitEmailSender.MapSecurity(security));
    }

    [Fact]
    public void MapSecurity_UnknownValue_Throws()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => MailKitEmailSender.MapSecurity((SmtpSecurity)99));

        Assert.Equal("security", exception.ParamName);
    }
}