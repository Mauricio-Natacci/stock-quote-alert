using StockQuoteAlert.Core.Domain;

namespace StockQuoteAlert.Core.Application.Journal;

/// <param name="Channel">How the alert was delivered (e.g. "E-mail").</param>
/// <param name="Body">The plain-text content that was sent.</param>
/// <param name="Error">Why delivery failed; <see langword="null"/> when it was sent.</param>
public sealed record NotificationRecord(
    Guid Id,
    PriceAlert Alert,
    string Channel,
    string Recipient,
    string Subject,
    string Body,
    DateTimeOffset AttemptedAt,
    NotificationStatus Status,
    string? Error = null);