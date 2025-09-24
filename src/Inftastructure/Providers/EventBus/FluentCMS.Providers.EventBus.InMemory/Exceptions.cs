namespace FluentCMS.Providers.EventBus.InMemory;

[Serializable]
public class EventPublisherAggregatedException<TEvent> : AggregateException where TEvent : class, IEvent
{
    public EventPublisherAggregatedException(IEnumerable<Exception> innerExceptions) : base($"One or more {typeof(TEvent).Name} event handlers threw an exception.", innerExceptions)
    {
    }
}
