using System.Collections.Concurrent;
using System.Reflection;
using FluentCMS.Core.Content.Hooks;
using FluentCMS.Core.Plugins;
using FluentCMS.Core.Telemetry;
using FluentCMS.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.Events;

// Runs the handlers of an event in this process, in order. The write has committed by the time this runs, so a handler
// that throws changes nothing: the failure is logged and the next handler runs. A handler that cannot even be built
// (a constructor that throws) is the same failure for the handlers of that event, since the container builds them together.
// The token is the host's shutdown, never the request's, which has ended or may end at any time, and is also cancelled when
// the handler outlives EventHandlerOptions.Timeout. A handler of a plugin disabled in the event's site does not run.
// The handlers run in a scope of their own, never the caller's, so that one left running past its timeout shares no context
// with the caller; the handlers after it get a fresh scope, and its own scope lasts until it ends.
internal sealed class EventDispatcher(
    IServiceScopeFactory scopes,
    PluginState plugins,
    EventHandlerOptions options,
    TimeProvider time,
    ILogger<EventDispatcher> logger,
    IHostApplicationLifetime? lifetime = null)
{
    private static readonly ConcurrentDictionary<Type, Func<EventDispatcher, CmsEvent, ValueTask>> Dispatchers = new();

    private static readonly MethodInfo DispatchMethod = typeof(EventDispatcher).GetMethod(nameof(DispatchTypedAsync), BindingFlags.Instance | BindingFlags.NonPublic)!;

    public ValueTask DispatchAsync(CmsEvent @event)
    {
        ArgumentNullException.ThrowIfNull(@event);

        return Dispatchers.GetOrAdd(
            @event.GetType(),
            static type => DispatchMethod.MakeGenericMethod(type).CreateDelegate<Func<EventDispatcher, CmsEvent, ValueTask>>())(this, @event);
    }

    private async ValueTask DispatchTypedAsync<TEvent>(CmsEvent @event)
        where TEvent : CmsEvent
    {
        var typed = (TEvent)@event;
        var shutdown = lifetime?.ApplicationStopping ?? CancellationToken.None;
        List<int>? enabled = null;
        var next = 0;
        do
        {
            var scope = scopes.CreateAsyncScope();
            List<IEventHandler<TEvent>> handlers;
            try
            {
                handlers = HandlerOrder.Sort(scope.ServiceProvider.GetServices<IEventHandler<TEvent>>(), handler => handler.GetType(), scope.ServiceProvider.GetService<IHookTiebreaker>());
            }
            catch (Exception exception)
            {
                EventLog.HandlersUnavailable(logger, exception, typeof(TEvent).Name, @event.EventId);
                await DisposeAsync(scope, typeof(TEvent).Name, @event.EventId);
                return;
            }

            enabled ??= await EnabledAsync(handlers, @event.Source.SiteId, shutdown);
            next = await RunFromAsync(handlers, enabled, next, typed, scope, shutdown);
        }
        while (next < enabled.Count);
    }

    // The positions, in order, of the handlers whose plugin is enabled in the site. A fresh scope builds the same handlers
    // in the same order, so the positions hold there too.
    private async ValueTask<List<int>> EnabledAsync<TEvent>(List<IEventHandler<TEvent>> handlers, Guid site, CancellationToken shutdown)
        where TEvent : CmsEvent
    {
        List<int> all = [.. Enumerable.Range(0, handlers.Count)];
        try
        {
            return await plugins.EnabledAsync(all, index => handlers[index].GetType(), site, shutdown);
        }
        catch (Exception exception)
        {
            // Without the state no plugin's handler may run, but core's and the host's still do.
            PluginLog.StateUnavailable(logger, exception);
            return plugins.WithoutPlugins(all, index => handlers[index].GetType());
        }
    }

    // Runs the handlers from next on and returns where it stopped: at the end, having disposed the scope, or just past a
    // handler that was abandoned, which keeps the scope until it ends.
    private async Task<int> RunFromAsync<TEvent>(List<IEventHandler<TEvent>> handlers, List<int> enabled, int next, TEvent @event, AsyncServiceScope scope, CancellationToken shutdown)
        where TEvent : CmsEvent
    {
        while (next < enabled.Count)
        {
            if (!await RunAsync(handlers[enabled[next++]], @event, scope, shutdown))
            {
                return next;
            }
        }

        await DisposeAsync(scope, typeof(TEvent).Name, @event.EventId);
        return next;
    }

    // The caller waits for a handler only as long as the timeout. It runs off the caller's thread, so that one that blocks
    // before its first await is cut off too. False when it was abandoned, with its scope.
    private async Task<bool> RunAsync<TEvent>(IEventHandler<TEvent> handler, TEvent @event, AsyncServiceScope scope, CancellationToken shutdown)
        where TEvent : CmsEvent
    {
        var name = handler.GetType().FullName ?? handler.GetType().Name;
        var eventType = typeof(TEvent).Name;
        var started = time.GetTimestamp();
        var bound = CancellationTokenSource.CreateLinkedTokenSource(shutdown);
        var run = Task.Run(() => handler.HandleAsync(@event, bound.Token).AsTask(), CancellationToken.None);
        string outcome;
        try
        {
            await run.WaitAsync(options.Timeout, time, CancellationToken.None);
            outcome = "succeeded";
        }
        // A TimeoutException the handler threw itself is a failure like any other.
        catch (TimeoutException timeout) when (!ReferenceEquals(timeout, run.Exception?.InnerException))
        {
            Record(eventType, name, started, "timed-out");
            EventLog.HandlerAbandoned(logger, name, eventType, @event.EventId, options.Timeout);
            Abandon(run, bound, scope, name, eventType, @event.EventId);
            return false;
        }
        catch (Exception exception)
        {
            outcome = "failed";
            EventLog.HandlerFailed(logger, exception, name, eventType, @event.EventId);
        }

        bound.Dispose();
        var duration = Record(eventType, name, started, outcome);
        if (duration >= options.SlowThreshold)
        {
            EventLog.HandlerSlow(logger, name, eventType, @event.EventId, duration);
        }

        return true;
    }

    // A cooperative handler stops at its token. One that does not is left to end on its own, and whatever it does then,
    // such as touching the scope of a request that has ended, is logged and never thrown.
    private void Abandon(Task run, CancellationTokenSource bound, AsyncServiceScope scope, string handler, string eventType, Guid eventId) =>
        _ = Task.Run(
            async () =>
            {
                try
                {
                    await Task.WhenAll(bound.CancelAsync(), run);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception exception)
                {
                    EventLog.AbandonedHandlerFailed(logger, exception, handler, eventType, eventId);
                }
                finally
                {
                    bound.Dispose();
                    await DisposeAsync(scope, eventType, eventId);
                }
            },
            CancellationToken.None);

    // A scoped service that throws on disposal must not reach the caller either.
    private async Task DisposeAsync(AsyncServiceScope scope, string eventType, Guid eventId)
    {
        try
        {
            await scope.DisposeAsync();
        }
        catch (Exception exception)
        {
            EventLog.HandlerScopeFailed(logger, exception, eventType, eventId);
        }
    }

    private TimeSpan Record(string eventType, string handler, long started, string outcome)
    {
        var duration = time.GetElapsedTime(started);
        FluentCmsTelemetry.EventHandlerDuration.Record(
            duration.TotalSeconds,
            new KeyValuePair<string, object?>(FluentCmsTelemetry.Tags.Operation, eventType),
            new KeyValuePair<string, object?>(FluentCmsTelemetry.Tags.Handler, handler),
            new KeyValuePair<string, object?>(FluentCmsTelemetry.Tags.Outcome, outcome));
        return duration;
    }
}
