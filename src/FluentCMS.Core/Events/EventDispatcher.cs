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
internal sealed class EventDispatcher(
    IServiceProvider services,
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
        List<IEventHandler<TEvent>> handlers;
        try
        {
            handlers = HandlerOrder.Sort(services.GetServices<IEventHandler<TEvent>>(), handler => handler.GetType(), services.GetService<IHookTiebreaker>());
        }
        catch (Exception exception)
        {
            EventLog.HandlersUnavailable(logger, exception, typeof(TEvent).Name, @event.EventId);
            return;
        }

        try
        {
            handlers = await plugins.EnabledAsync(handlers, handler => handler.GetType(), @event.Source.SiteId, shutdown);
        }
        catch (Exception exception)
        {
            // Without the state no plugin's handler may run, but core's and the host's still do.
            PluginLog.StateUnavailable(logger, exception);
            handlers = plugins.WithoutPlugins(handlers, handler => handler.GetType());
        }
        foreach (var handler in handlers)
        {
            await RunAsync(handler, typed, shutdown);
        }
    }

    // The caller waits for a handler only as long as the timeout. It runs off the caller's thread, so that one that blocks
    // before its first await is cut off too.
    private async Task RunAsync<TEvent>(IEventHandler<TEvent> handler, TEvent @event, CancellationToken shutdown)
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
            Abandon(run, bound, name, eventType, @event.EventId);
            return;
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
    }

    // A cooperative handler stops at its token. One that does not is left to end on its own, and whatever it does then,
    // such as touching the scope of a request that has ended, is logged and never thrown.
    private void Abandon(Task run, CancellationTokenSource bound, string handler, string eventType, Guid eventId) =>
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
                }
            },
            CancellationToken.None);

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
