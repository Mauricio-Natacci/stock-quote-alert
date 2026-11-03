using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StockQuoteAlert.Core.Application.Abstractions;
using StockQuoteAlert.Core.Application.Alerts;
using StockQuoteAlert.Core.Application.Journal;
using StockQuoteAlert.Core.Domain;

namespace StockQuoteAlert.Infrastructure.Email;

/// <summary>
/// Composes the alert e-mail and sends it to the configured recipient. Every attempt, sent or failed, is recorded in
/// the journal; a failure is rethrown so the dispatcher retries it.
/// </summary>
internal sealed partial class EmailAlertConsumer(
    IAlertEmailComposer composer,
    IEmailSender sender,
    IMonitoringJournal journal,
    IOptions<EmailOptions> options,
    TimeProvider timeProvider,
    ILogger<EmailAlertConsumer> logger) : IAlertConsumer
{
    internal const string ChannelName = "E-mail";

    public string Name => ChannelName;

    public async Task ConsumeAsync(PriceAlertRaised alert, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(alert);

        var content = composer.Compose(alert.Alert);
        var message = new EmailMessage(options.Value.Recipient, content.Subject, content.TextBody, content.HtmlBody);

        try
        {
            await sender.SendAsync(message, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            journal.RecordNotification(CreateRecord(alert.Alert, message, NotificationStatus.Failed, exception.Message));
            throw;
        }

        journal.RecordNotification(CreateRecord(alert.Alert, message, NotificationStatus.Sent));
        LogSent(alert.Alert.Kind, alert.Alert.Ticker.Symbol, message.To);
    }

    private NotificationRecord CreateRecord(PriceAlert alert, EmailMessage message, NotificationStatus status, string? error = null) =>
        new(Guid.CreateVersion7(), alert, ChannelName, message.To, message.Subject, message.TextBody, timeProvider.GetUtcNow(), status, error);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Kind} alert e-mail for {Ticker} sent to {Recipient}")]
    private partial void LogSent(AlertKind kind, string ticker, string recipient);
}