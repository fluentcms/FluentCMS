using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.IntegrationTests.Diagnostics;

internal sealed record LogEntry(string Category, LogLevel Level, EventId EventId, string Message, IReadOnlyDictionary<string, object?> State);

// Keeps what the host logs, with the structured values of each message.
internal sealed class LogCapture : ILoggerProvider
{
    private readonly ConcurrentQueue<LogEntry> _entries = new();

    public IReadOnlyList<LogEntry> Entries => [.. _entries];

    public ILogger CreateLogger(string categoryName) => new CaptureLogger(categoryName, _entries);

    public void Dispose()
    {
    }

    private sealed class CaptureLogger(string category, ConcurrentQueue<LogEntry> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var values = state is IEnumerable<KeyValuePair<string, object?>> pairs
                ? pairs.ToDictionary(pair => pair.Key, pair => pair.Value)
                : [];
            entries.Enqueue(new LogEntry(category, logLevel, eventId, formatter(state, exception), values));
        }
    }
}
