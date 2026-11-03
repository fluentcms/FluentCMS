using System.Collections.Concurrent;
using FluentCMS.Core.Content.Hooks;
using Microsoft.EntityFrameworkCore;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Data.Tests.Content;
using FluentCMS.Events;
using FluentCMS.Hooks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Data.Tests.Events;

// What the handlers of a test saw, shared by every instance of the test, and what they should do next.
internal sealed class Seen
{
    public ConcurrentQueue<string> Calls { get; } = new();

    public ConcurrentQueue<CmsEvent> Events { get; } = new();

    public ConcurrentQueue<EventDelivery> Deliveries { get; } = new();

    public ConcurrentQueue<CancellationToken> Tokens { get; } = new();

    // Runs inside the handler, so a test can look at the database at the moment the event arrives or make the handler fail.
    public Func<CmsEvent, EventDelivery?, Task> Behavior { get; set; } = (_, _) => Task.CompletedTask;

    public IEnumerable<T> Of<T>()
        where T : CmsEvent => Events.OfType<T>();

    public async ValueTask HeardAsync(string name, CmsEvent @event, EventDelivery? delivery, CancellationToken token)
    {
        Tokens.Enqueue(token);
        Calls.Enqueue(name);
        Events.Enqueue(@event);
        if (delivery is not null)
        {
            Deliveries.Enqueue(delivery);
        }

        await Behavior(@event, delivery);
    }
}

internal sealed class SavedHandler(Seen seen) : IEventHandler<EntryDraftSavedEvent>
{
    public ValueTask HandleAsync(EntryDraftSavedEvent message, CancellationToken cancellationToken) => seen.HeardAsync("saved", message, null, cancellationToken);
}

internal sealed class RestoredHandler(Seen seen) : IEventHandler<EntryRestoredEvent>
{
    public ValueTask HandleAsync(EntryRestoredEvent message, CancellationToken cancellationToken) => seen.HeardAsync("restored", message, null, cancellationToken);
}

// Writes another entry while it is being told of the first, once.
internal sealed class WritingHandler(Seen seen, FluentCMS.Content.IContentService content) : IEventHandler<EntryDraftSavedEvent>
{
    public async ValueTask HandleAsync(EntryDraftSavedEvent message, CancellationToken cancellationToken)
    {
        await seen.HeardAsync("writing", message, null, cancellationToken);
        if (seen.Calls.Count(call => call == "writing") == 1)
        {
            await content.CreateAsync("article", new FluentCMS.Content.ContentDocument { ["title"] = "From a handler" }, cancellationToken);
        }
    }
}

internal sealed class TrashedHandler(Seen seen) : IEventHandler<EntryTrashedEvent>
{
    public ValueTask HandleAsync(EntryTrashedEvent message, CancellationToken cancellationToken) => seen.HeardAsync("trashed", message, null, cancellationToken);
}

[HookOrder(2)]
internal sealed class LateSaved(Seen seen) : IEventHandler<EntryDraftSavedEvent>
{
    public ValueTask HandleAsync(EntryDraftSavedEvent message, CancellationToken cancellationToken) => seen.HeardAsync("late", message, null, cancellationToken);
}

[HookOrder(-1)]
internal sealed class EarlySaved(Seen seen) : IEventHandler<EntryDraftSavedEvent>
{
    public ValueTask HandleAsync(EntryDraftSavedEvent message, CancellationToken cancellationToken) => seen.HeardAsync("early", message, null, cancellationToken);
}

internal sealed class TiedFirstRegistered(Seen seen) : IEventHandler<EntryDraftSavedEvent>
{
    public ValueTask HandleAsync(EntryDraftSavedEvent message, CancellationToken cancellationToken) => seen.HeardAsync("tied-first-registered", message, null, cancellationToken);
}

internal sealed class TiedLastRegistered(Seen seen) : IEventHandler<EntryDraftSavedEvent>
{
    public ValueTask HandleAsync(EntryDraftSavedEvent message, CancellationToken cancellationToken) => seen.HeardAsync("tied-last-registered", message, null, cancellationToken);
}

internal sealed class RankTiebreaker : IHookTiebreaker
{
    public int Rank(Type hookType) => hookType == typeof(TiedLastRegistered) ? -1 : 0;
}

internal sealed class SavedOutbox(Seen seen) : IOutboxEventHandler<EntryDraftSavedEvent>
{
    public ValueTask HandleAsync(EntryDraftSavedEvent message, EventDelivery delivery, CancellationToken cancellationToken) => seen.HeardAsync("outbox", message, delivery, cancellationToken);
}

[HookOrder(3)]
internal sealed class OutboxThird(Seen seen) : IOutboxEventHandler<EntryDraftSavedEvent>
{
    public ValueTask HandleAsync(EntryDraftSavedEvent message, EventDelivery delivery, CancellationToken cancellationToken) => seen.HeardAsync("outbox-3", message, delivery, cancellationToken);
}

[HookOrder(1)]
internal sealed class OutboxFirst(Seen seen) : IOutboxEventHandler<EntryDraftSavedEvent>
{
    public ValueTask HandleAsync(EntryDraftSavedEvent message, EventDelivery delivery, CancellationToken cancellationToken) => seen.HeardAsync("outbox-1", message, delivery, cancellationToken);
}

[HookOrder(2)]
internal sealed class OutboxSecond(Seen seen) : IOutboxEventHandler<EntryDraftSavedEvent>
{
    public ValueTask HandleAsync(EntryDraftSavedEvent message, EventDelivery delivery, CancellationToken cancellationToken) => seen.HeardAsync("outbox-2", message, delivery, cancellationToken);
}

internal sealed class TrashedOutbox(Seen seen) : IOutboxEventHandler<EntryTrashedEvent>
{
    public ValueTask HandleAsync(EntryTrashedEvent message, EventDelivery delivery, CancellationToken cancellationToken) => seen.HeardAsync("trashed-outbox", message, delivery, cancellationToken);
}

internal sealed record Ping(string Note) : CmsEvent;

internal sealed record Ping2(string Note) : CmsEvent;

internal sealed class Ping2Handler(Seen seen) : IEventHandler<Ping2>
{
    public ValueTask HandleAsync(Ping2 message, CancellationToken cancellationToken) => seen.HeardAsync("ping2", message, null, cancellationToken);
}

// A plugin whose handler cannot be built.
internal sealed class BrokenPingHandler : IEventHandler<Ping>
{
    public BrokenPingHandler() => throw new InvalidOperationException("ctor of a plugin handler failed");

    public ValueTask HandleAsync(Ping message, CancellationToken cancellationToken) => ValueTask.CompletedTask;
}

internal sealed class BrokenSavedHandler : IEventHandler<EntryDraftSavedEvent>
{
    public BrokenSavedHandler() => throw new InvalidOperationException("ctor of a plugin handler failed");

    public ValueTask HandleAsync(EntryDraftSavedEvent message, CancellationToken cancellationToken) => ValueTask.CompletedTask;
}

internal sealed class PingHandler(Seen seen) : IEventHandler<Ping>
{
    public ValueTask HandleAsync(Ping message, CancellationToken cancellationToken) => seen.HeardAsync("ping", message, null, cancellationToken);
}

internal sealed class PingOutbox(Seen seen) : IOutboxEventHandler<Ping>
{
    public ValueTask HandleAsync(Ping message, EventDelivery delivery, CancellationToken cancellationToken) => seen.HeardAsync("ping-outbox", message, delivery, cancellationToken);
}

internal static class EventRegistrations
{
    public static Action<FluentCmsBuilder> With(Seen seen, Action<FluentCmsBuilder>? more = null) => cms =>
    {
        cms.Services.AddSingleton(seen);
        more?.Invoke(cms);
    };

    public static Action<FluentCmsBuilder> Outbox(Seen seen, Action<FluentCmsBuilder>? more = null) => With(seen, cms =>
    {
        cms.Events.AddOutboxHandler<EntryDraftSavedEvent, SavedOutbox>("saved-outbox");
        cms.Events.Outbox.RetryBaseDelay = TimeSpan.Zero;
        cms.Events.Outbox.RetryMaxDelay = TimeSpan.Zero;
        cms.Events.Outbox.RetryJitter = 0;
        more?.Invoke(cms);
    });

    public static List<OutboxMessage> Rows(this Content.ServiceDatabase database) =>
        database.Read(context => context.Set<OutboxMessage>().AsNoTracking().OrderBy(row => row.Sequence).ToList());
}

internal sealed class EventLogs : ILoggerProvider
{
    private readonly ConcurrentDictionary<string, int> _events = new(StringComparer.Ordinal);

    public int Count(string eventName) => _events.GetValueOrDefault(eventName);

    public ILogger CreateLogger(string categoryName) => new Logger(_events);

    public void Dispose()
    {
    }

    private sealed class Logger(ConcurrentDictionary<string, int> events) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (eventId.Name is { } name)
            {
                events.AddOrUpdate(name, 1, (_, count) => count + 1);
            }
        }
    }
}
