namespace FluentCMS.Providers.EventBus.Abstractions;

// Generic event subscriber interface
public interface IEventSubscriber<TEvent> where TEvent : class, IEvent
{
    Task Handle(TEvent domainEvent, CancellationToken cancellationToken = default);
}