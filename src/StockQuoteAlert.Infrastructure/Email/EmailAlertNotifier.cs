using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StockQuoteAlert.Core.Application.Abstractions;
using StockQuoteAlert.Core.Domain;

namespace StockQuoteAlert.Infrastructure.Email;

/// <summary>
/// Composes the alert e-mail and sends it to the configured recipient. A failed delivery is logged and reported as
/// <see langword="false"/>; an alert that cannot be composed is a programming error and still throws.
/// </summary>
internal sealed partial class EmailAlertNotifier(
    IAlertEmailComposer composer,
    IEmailSender sender,
    IOptions<EmailOptions> options,
    ILogger<EmailAlertNotifier> logger) : IAlertNotifier
{
    public async Task<bool> NotifyAsync(PriceAlert alert, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(alert);

        var content = composer.Compose(alert);
        var message = new EmailMessage(options.Value.Recipient, content.Subject, content.TextBody);

        try
        {
            await sender.SendAsync(message, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogFailed(exception, alert.Kind, alert.Ticker.Symbol, message.To);
            return false;
        }

        LogSent(alert.Kind, alert.Ticker.Symbol, message.To);
        return true;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "{Kind} alert e-mail for {Ticker} sent to {Recipient}")]
    private partial void LogSent(AlertKind kind, string ticker, string recipient);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not send {Kind} alert e-mail for {Ticker} to {Recipient}")]
    private partial void LogFailed(Exception exception, AlertKind kind, string ticker, string recipient);
}