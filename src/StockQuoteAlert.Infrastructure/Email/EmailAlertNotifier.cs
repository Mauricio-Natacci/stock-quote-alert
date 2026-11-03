using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StockQuoteAlert.Core.Application.Abstractions;
using StockQuoteAlert.Core.Application.Journal;
using StockQuoteAlert.Core.Domain;

namespace StockQuoteAlert.Infrastructure.Email;

/// <summary>
/// Composes the alert e-mail and sends it to the configured recipient. A failed delivery is logged and reported as
/// <see langword="false"/>; an alert that cannot be composed is a programming error and still throws. Every attempt,
/// sent or failed, is recorded in the journal with the content, recipient and time.
/// </summary>
internal sealed partial class EmailAlertNotifier(
    IAlertEmailComposer composer,
    IEmailSender sender,
    IOptions<EmailOptions> options,
    IMonitoringJournal journal,
    TimeProvider timeProvider,
    ILogger<EmailAlertNotifier> logger) : IAlertNotifier
{
    internal const string ChannelName = "E-mail";

    public async Task<bool> NotifyAsync(PriceAlert alert, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(alert);

        var content = composer.Compose(alert);
        var message = new EmailMessage(options.Value.Recipient, content.Subject, content.TextBody, content.HtmlBody);

        try
        {
            await sender.SendAsync(message, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            journal.RecordNotification(CreateRecord(alert, message, NotificationStatus.Failed, exception.Message));
            LogFailed(exception, alert.Kind, alert.Ticker.Symbol, message.To);
            return false;
        }

        journal.RecordNotification(CreateRecord(alert, message, NotificationStatus.Sent));
        LogSent(alert.Kind, alert.Ticker.Symbol, message.To);
        return true;
    }

    private NotificationRecord CreateRecord(PriceAlert alert, EmailMessage message, NotificationStatus status, string? error = null) =>
        new(Guid.CreateVersion7(), alert, ChannelName, message.To, message.Subject, message.TextBody, timeProvider.GetUtcNow(), status, error);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Kind} alert e-mail for {Ticker} sent to {Recipient}")]
    private partial void LogSent(AlertKind kind, string ticker, string recipient);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not send {Kind} alert e-mail for {Ticker} to {Recipient}")]
    private partial void LogFailed(Exception exception, AlertKind kind, string ticker, string recipient);
}