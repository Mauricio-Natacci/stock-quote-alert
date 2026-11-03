using StockQuoteAlert.Core.Application.Journal;
using StockQuoteAlert.Core.Domain;

namespace StockQuoteAlert.Cli.Dashboard;

internal sealed record NotificationResponse(
    Guid Id,
    string Ticker,
    AlertKind Action,
    decimal Price,
    decimal ReferencePrice,
    DateTimeOffset QuotedAt,
    string Channel,
    string Recipient,
    string Subject,
    string Body,
    DateTimeOffset AttemptedAt,
    NotificationStatus Status,
    string? Error)
{
    public static NotificationResponse From(NotificationRecord record) => new(
        record.Id,
        record.Alert.Ticker.Symbol,
        record.Alert.Kind,
        record.Alert.CurrentPrice,
        record.Alert.ReferencePrice,
        record.Alert.QuotedAt,
        record.Channel,
        record.Recipient,
        record.Subject,
        record.Body,
        record.AttemptedAt,
        record.Status,
        record.Error);
}