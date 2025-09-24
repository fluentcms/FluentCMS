namespace FluentCMS.Providers.EventBus.Abstractions;

// Generic event publisher interface
public interface IEventPublisher
{
    Task Publish<TEvent>(TEvent data, CancellationToken cancellationToken = default) where TEvent : class, IEvent;
}
