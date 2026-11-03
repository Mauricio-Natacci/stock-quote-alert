using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace StockQuoteAlert.Tests.TestDoubles;

/// <summary>Records every log entry so tests can check level, message, exception and structured properties.</summary>
internal sealed class FakeLogger<T> : ILogger<T>
{
    public ConcurrentQueue<LogEntry> Entries { get; } = new();

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);

        var properties = state is IEnumerable<KeyValuePair<string, object?>> pairs
            ? pairs.Where(pair => pair.Key != "{OriginalFormat}").ToDictionary(pair => pair.Key, pair => pair.Value)
            : [];

        Entries.Enqueue(new LogEntry(logLevel, formatter(state, exception), exception, properties, eventId.Name));
    }
}

/// <param name="Properties">The named values of a structured entry (e.g. Price, Ticker), as passed to the logger.</param>
/// <param name="EventName">The event's name, when it has one (e.g. <c>CheckCompleted</c>).</param>
internal sealed record LogEntry(LogLevel Level, string Message, Exception? Exception, IReadOnlyDictionary<string, object?> Properties, string? EventName);