using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using StockQuoteAlert.Core.Application.Journal;
using StockQuoteAlert.Core.Domain;
using StockQuoteAlert.Infrastructure.Email;
using StockQuoteAlert.Infrastructure.Journal;
using StockQuoteAlert.Tests.TestDoubles;

namespace StockQuoteAlert.Tests.Infrastructure.Email;

/// <summary>Runs the real composer and MailKit sender; only the SMTP server is faked.</summary>
public sealed class EmailAlertConsumerTests
{
    private readonly FakeSmtpServer _smtp = new();
    private readonly FakeLogger<EmailAlertConsumer> _logger = new();
    private readonly InMemoryMonitoringJournal _journal = new();
    private readonly FakeTimeProvider _time = new(TestData.Now.AddSeconds(3));
    private readonly EmailAlertConsumer _consumer;

    public EmailAlertConsumerTests()
    {
        var emailOptions = Options.Create(TestData.EmailOptions);
        var sender = new MailKitEmailSender(_smtp, Options.Create(TestData.SmtpOptions), emailOptions);

        _consumer = new EmailAlertConsumer(new AlertEmailComposer(emailOptions), sender, _journal, emailOptions, _time, _logger);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Name_IsEmail()
    {
        Assert.Equal("E-mail", _consumer.Name);
    }

    [Fact]
    public async Task Consume_DeliversTheComposedEmailToTheConfiguredRecipient()
    {
        var alert = TestData.SellAlert(22.70m);

        await _consumer.ConsumeAsync(TestData.Raised(alert), Ct);

        var email = Assert.Single(_smtp.Delivered);
        var expected = new AlertEmailComposer(Options.Create(TestData.EmailOptions)).Compose(alert);
        Assert.Equal("dest@gmail.com", email.To);
        Assert.Equal("me@gmail.com", email.FromAddress);
        Assert.Equal(expected.Subject, email.Subject);
        Assert.Equal(expected.TextBody.ReplaceLineEndings(), email.Body.ReplaceLineEndings());
        Assert.True(email.IsAlternative);
        Assert.Equal(expected.HtmlBody.ReplaceLineEndings(), email.HtmlBody?.ReplaceLineEndings());
    }

    [Fact]
    public async Task Consume_OnSuccess_LogsAndRecordsASentNotification()
    {
        var alert = TestData.BuyAlert(22.50m);

        await _consumer.ConsumeAsync(TestData.Raised(alert), Ct);

        var entry = Assert.Single(_logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal("Buy alert e-mail for PETR4 sent to dest@gmail.com", entry.Message);
        Assert.Equal(AlertKind.Buy, entry.Properties["Kind"]);
        Assert.Equal("dest@gmail.com", entry.Properties["Recipient"]);

        var record = Assert.Single(_journal.GetSnapshot().Notifications);
        var email = Assert.Single(_smtp.Delivered);
        Assert.Equal(NotificationStatus.Sent, record.Status);
        Assert.Equal(alert, record.Alert);
        Assert.Equal("E-mail", record.Channel);
        Assert.Equal(email.Subject, record.Subject);
        Assert.Equal(email.Body.ReplaceLineEndings(), record.Body.ReplaceLineEndings());
        Assert.Equal(TestData.Now.AddSeconds(3), record.AttemptedAt);
        Assert.Null(record.Error);
    }

    public static TheoryData<Exception> SmtpFailures => new()
    {
        new IOException("Connection refused"),
        new SmtpProtocolException("unexpected reply"),
        new AuthenticationException("535 bad credentials"),
    };

    [Theory]
    [MemberData(nameof(SmtpFailures))]
    public async Task Consume_WhenSendingFails_RecordsTheFailureAndRethrowsItForRetry(Exception failure)
    {
        if (failure is AuthenticationException)
        {
            _smtp.AuthenticationFailure = failure;
        }
        else
        {
            _smtp.ConnectionFailure = failure;
        }

        var thrown = await Assert.ThrowsAnyAsync<Exception>(() => _consumer.ConsumeAsync(TestData.Raised(TestData.SellAlert()), Ct));

        Assert.Same(failure, thrown);
        Assert.Empty(_smtp.Delivered);
        var record = Assert.Single(_journal.GetSnapshot().Notifications);
        Assert.Equal(NotificationStatus.Failed, record.Status);
        Assert.Equal(failure.Message, record.Error);
        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public async Task Consume_WhenServerRejectsMessage_RecordsTheFailure()
    {
        _smtp.SendFailure = new SmtpCommandException(SmtpErrorCode.RecipientNotAccepted, SmtpStatusCode.MailboxUnavailable, "550 no such user");

        await Assert.ThrowsAsync<SmtpCommandException>(() => _consumer.ConsumeAsync(TestData.Raised(TestData.SellAlert()), Ct));

        Assert.Equal("550 no such user", Assert.Single(_journal.GetSnapshot().Notifications).Error);
    }

    [Fact]
    public async Task Consume_EveryAttempt_GetsItsOwnRecord()
    {
        var raised = TestData.Raised(TestData.SellAlert());
        _smtp.ConnectionFailure = new IOException("Connection refused");
        await Assert.ThrowsAsync<IOException>(() => _consumer.ConsumeAsync(raised, Ct));
        _smtp.ConnectionFailure = null;

        await _consumer.ConsumeAsync(raised, Ct);

        var records = _journal.GetSnapshot().Notifications;
        Assert.Equal([NotificationStatus.Failed, NotificationStatus.Sent], records.Select(record => record.Status));
        Assert.NotEqual(records[0].Id, records[1].Id);
    }

    [Fact]
    public async Task Consume_WhenCancelled_PropagatesAndRecordsNothing()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        _smtp.ConnectionFailure = new OperationCanceledException(cancellation.Token);

        await Assert.ThrowsAsync<OperationCanceledException>(() => _consumer.ConsumeAsync(TestData.Raised(TestData.SellAlert()), cancellation.Token));

        Assert.Empty(_journal.GetSnapshot().Notifications);
        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public async Task Consume_WhenSendTimesOutWithoutCancellation_RecordsAFailure()
    {
        _smtp.ConnectionFailure = new OperationCanceledException("The operation has timed out.");

        await Assert.ThrowsAsync<OperationCanceledException>(() => _consumer.ConsumeAsync(TestData.Raised(TestData.SellAlert()), Ct));

        Assert.Equal(NotificationStatus.Failed, Assert.Single(_journal.GetSnapshot().Notifications).Status);
    }

    [Fact]
    public async Task Consume_WithUncomposableAlert_ThrowsBeforeContactingTheServer()
    {
        var raised = TestData.Raised(TestData.SellAlert() with { Kind = (AlertKind)42 });

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _consumer.ConsumeAsync(raised, Ct));

        Assert.Empty(_smtp.Clients);
        Assert.Empty(_journal.GetSnapshot().Notifications);
    }

    [Fact]
    public async Task Consume_WithNullAlert_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _consumer.ConsumeAsync(null!, Ct));

        Assert.Empty(_smtp.Clients);
    }
}