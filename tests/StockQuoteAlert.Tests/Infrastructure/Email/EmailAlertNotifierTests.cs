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
public sealed class EmailAlertNotifierTests
{
    private readonly FakeSmtpServer _smtp = new();
    private readonly FakeLogger<EmailAlertNotifier> _logger = new();
    private readonly InMemoryMonitoringJournal _journal = new();
    private readonly FakeTimeProvider _time = new(TestData.Now.AddSeconds(3));
    private readonly EmailAlertNotifier _notifier;

    public EmailAlertNotifierTests()
    {
        var emailOptions = Options.Create(TestData.EmailOptions);
        var sender = new MailKitEmailSender(_smtp, Options.Create(TestData.SmtpOptions), emailOptions);

        _notifier = new EmailAlertNotifier(new AlertEmailComposer(emailOptions), sender, emailOptions, _journal, _time, _logger);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task NotifyAsync_SellAlert_DeliversComposedEmailToConfiguredRecipient()
    {
        var alert = TestData.SellAlert(22.70m);

        var delivered = await _notifier.NotifyAsync(alert, Ct);

        Assert.True(delivered);
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
    public async Task NotifyAsync_BuyAlert_DeliversBuyEmail()
    {
        var delivered = await _notifier.NotifyAsync(TestData.BuyAlert(22.50m), Ct);

        Assert.True(delivered);
        Assert.Equal("[COMPRA] PETR4 caiu para R$ 22,50, 0,40% abaixo da referência de compra", Assert.Single(_smtp.Delivered).Subject);
    }

    [Fact]
    public async Task NotifyAsync_OnSuccess_LogsInformation()
    {
        await _notifier.NotifyAsync(TestData.SellAlert(), Ct);

        var entry = Assert.Single(_logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal("Sell alert e-mail for PETR4 sent to dest@gmail.com", entry.Message);
        Assert.Null(entry.Exception);
        Assert.Equal(AlertKind.Sell, entry.Properties["Kind"]);
        Assert.Equal("PETR4", entry.Properties["Ticker"]);
        Assert.Equal("dest@gmail.com", entry.Properties["Recipient"]);
    }

    public static TheoryData<Exception> SmtpFailures => new()
    {
        new IOException("Connection refused"),
        new SmtpProtocolException("unexpected reply"),
    };

    [Theory]
    [MemberData(nameof(SmtpFailures))]
    public async Task NotifyAsync_WhenConnectionFails_ReturnsFalseAndLogsWarning(Exception failure)
    {
        _smtp.ConnectionFailure = failure;

        var delivered = await _notifier.NotifyAsync(TestData.SellAlert(), Ct);

        Assert.False(delivered);
        Assert.Empty(_smtp.Delivered);
        var entry = Assert.Single(_logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal("Could not send Sell alert e-mail for PETR4 to dest@gmail.com", entry.Message);
        Assert.Same(failure, entry.Exception);
    }

    [Fact]
    public async Task NotifyAsync_WhenAuthenticationFails_ReturnsFalse()
    {
        _smtp.AuthenticationFailure = new AuthenticationException("535 bad credentials");

        var delivered = await _notifier.NotifyAsync(TestData.BuyAlert(), Ct);

        Assert.False(delivered);
        Assert.Empty(_smtp.Delivered);
        Assert.IsType<AuthenticationException>(Assert.Single(_logger.Entries).Exception);
    }

    [Fact]
    public async Task NotifyAsync_WhenServerRejectsMessage_ReturnsFalse()
    {
        _smtp.SendFailure = new SmtpCommandException(SmtpErrorCode.RecipientNotAccepted, SmtpStatusCode.MailboxUnavailable, "550 no such user");

        var delivered = await _notifier.NotifyAsync(TestData.SellAlert(), Ct);

        Assert.False(delivered);
        Assert.Equal(LogLevel.Warning, Assert.Single(_logger.Entries).Level);
    }

    [Fact]
    public async Task NotifyAsync_WhenSendTimesOutWithoutCancellation_ReturnsFalse()
    {
        _smtp.ConnectionFailure = new OperationCanceledException("The operation has timed out.");

        var delivered = await _notifier.NotifyAsync(TestData.SellAlert(), Ct);

        Assert.False(delivered);
        Assert.IsType<OperationCanceledException>(Assert.Single(_logger.Entries).Exception);
    }

    [Fact]
    public async Task NotifyAsync_WhenCancelled_PropagatesWithoutLogging()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        _smtp.ConnectionFailure = new OperationCanceledException(cancellation.Token);

        await Assert.ThrowsAsync<OperationCanceledException>(() => _notifier.NotifyAsync(TestData.SellAlert(), cancellation.Token));

        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public async Task NotifyAsync_AfterFailure_SucceedsOnNextCall()
    {
        _smtp.ConnectionFailure = new IOException("Connection refused");
        Assert.False(await _notifier.NotifyAsync(TestData.SellAlert(), Ct));

        _smtp.ConnectionFailure = null;

        Assert.True(await _notifier.NotifyAsync(TestData.SellAlert(), Ct));
        Assert.Single(_smtp.Delivered);
    }

    [Fact]
    public async Task NotifyAsync_OnSuccess_RecordsASentNotificationMatchingTheEmail()
    {
        var alert = TestData.BuyAlert(22.50m);

        await _notifier.NotifyAsync(alert, Ct);

        var record = Assert.Single(_journal.GetSnapshot().Notifications);
        var email = Assert.Single(_smtp.Delivered);
        Assert.Equal(NotificationStatus.Sent, record.Status);
        Assert.Equal(alert, record.Alert);
        Assert.Equal("E-mail", record.Channel);
        Assert.Equal(email.To, record.Recipient);
        Assert.Equal(email.Subject, record.Subject);
        Assert.Equal(email.Body.ReplaceLineEndings(), record.Body.ReplaceLineEndings());
        Assert.Equal(TestData.Now.AddSeconds(3), record.AttemptedAt);
        Assert.Null(record.Error);
        Assert.NotEqual(Guid.Empty, record.Id);
    }

    [Fact]
    public async Task NotifyAsync_OnFailure_RecordsAFailedNotificationWithTheError()
    {
        _smtp.ConnectionFailure = new IOException("Connection refused");

        await _notifier.NotifyAsync(TestData.SellAlert(), Ct);

        var record = Assert.Single(_journal.GetSnapshot().Notifications);
        Assert.Equal(NotificationStatus.Failed, record.Status);
        Assert.Equal("Connection refused", record.Error);
        Assert.StartsWith("[VENDA]", record.Subject, StringComparison.Ordinal);
        Assert.Equal("dest@gmail.com", record.Recipient);
    }

    [Fact]
    public async Task NotifyAsync_EveryAttempt_GetsItsOwnRecord()
    {
        _smtp.ConnectionFailure = new IOException("Connection refused");
        await _notifier.NotifyAsync(TestData.SellAlert(), Ct);
        _smtp.ConnectionFailure = null;
        await _notifier.NotifyAsync(TestData.SellAlert(), Ct);

        var records = _journal.GetSnapshot().Notifications;
        Assert.Equal([NotificationStatus.Failed, NotificationStatus.Sent], records.Select(record => record.Status));
        Assert.NotEqual(records[0].Id, records[1].Id);
    }

    [Fact]
    public async Task NotifyAsync_WhenCancelled_RecordsNothing()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        _smtp.ConnectionFailure = new OperationCanceledException(cancellation.Token);

        await Assert.ThrowsAsync<OperationCanceledException>(() => _notifier.NotifyAsync(TestData.SellAlert(), cancellation.Token));

        Assert.Empty(_journal.GetSnapshot().Notifications);
    }

    [Fact]
    public async Task NotifyAsync_WithUncomposableAlert_Throws()
    {
        var alert = TestData.SellAlert() with { Kind = (AlertKind)42 };

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _notifier.NotifyAsync(alert, Ct));

        Assert.Empty(_smtp.Clients);
    }

    [Fact]
    public async Task NotifyAsync_WithNullAlert_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _notifier.NotifyAsync(null!, Ct));

        Assert.Empty(_smtp.Clients);
    }
}