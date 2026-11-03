using Microsoft.Extensions.Logging;

namespace StockQuoteAlert.Tests.TestDoubles;

/// <param name="Properties">The named values of a structured entry (e.g. Price, Ticker), as passed to the logger.</param>
/// <param name="EventName">The event's name, when it has one (e.g. <c>CheckCompleted</c>).</param>
internal sealed record LogEntry(LogLevel Level, string Message, Exception? Exception, IReadOnlyDictionary<string, object?> Properties, string? EventName);