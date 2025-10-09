namespace FluentCMS.Infrastructure.EventBus.InMemory;

public class EventPublisherAggregatedException<TEvent>(IEnumerable<Exception> innerExceptions) :
    AggregateException($"One or more {typeof(TEvent).Name} event handlers threw an exception.", innerExceptions)
    where TEvent : class, IEvent
{
    public Type EventType => typeof(TEvent);
}
