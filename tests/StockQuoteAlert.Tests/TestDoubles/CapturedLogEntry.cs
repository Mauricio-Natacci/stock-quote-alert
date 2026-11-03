using Microsoft.Extensions.Logging;

namespace StockQuoteAlert.Tests.TestDoubles;

internal sealed record CapturedLogEntry(string Category, LogLevel Level, string Message, Exception? Exception, string? EventName);