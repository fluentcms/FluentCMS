using System.Collections.Concurrent;
using System.Reflection;
using FluentCMS.Core.Content.Hooks;
using FluentCMS.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.Events;

// Runs the handlers of an event in this process, in order. The write has committed by the time this runs, so a handler
// that throws changes nothing: the failure is logged and the next handler runs. A handler that cannot even be built
// (a constructor that throws) is the same failure for the handlers of that event, since the container builds them together.
// The token is the host's shutdown, never the request's, which has ended or may end at any time.
internal sealed class EventDispatcher(IServiceProvider services, ILogger<EventDispatcher> logger, IHostApplicationLifetime? lifetime = null)
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

        var shutdown = lifetime?.ApplicationStopping ?? CancellationToken.None;
        foreach (var handler in handlers)
        {
            try
            {
                await handler.HandleAsync(typed, shutdown);
            }
            catch (Exception exception)
            {
                EventLog.HandlerFailed(logger, exception, handler.GetType().FullName ?? handler.GetType().Name, typeof(TEvent).Name, @event.EventId);
            }
        }
    }
}
