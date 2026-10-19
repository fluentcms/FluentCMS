using System.Collections.Concurrent;
using FluentCMS.Core.Identity;
using Microsoft.Extensions.Logging;

namespace FluentCMS.IntegrationTests;

// Stands in for the host's ICurrentInitiator, which Infrastructure expects the host to register.
internal sealed class TestInitiator(Initiator? current) : ICurrentInitiator
{
    public Initiator? Current { get; } = current;
}

internal sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow()
    {
        return now;
    }
}

internal sealed class ManualClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow()
    {
        return Now;
    }
}

// Everything logged at any level, so a test can show a secret never reaches a log.
internal sealed class CapturedLogs : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _messages = new();

    public IReadOnlyCollection<string> Messages => _messages;

    public ILogger CreateLogger(string categoryName)
    {
        return new Logger(this);
    }

    public void Dispose()
    {
    }

    private sealed class Logger(CapturedLogs owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            owner._messages.Enqueue(formatter(state, exception) + exception);
        }
    }
}
