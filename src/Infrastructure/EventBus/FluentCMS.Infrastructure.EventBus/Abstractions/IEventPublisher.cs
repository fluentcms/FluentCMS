namespace FluentCMS.Infrastructure.EventBus.Abstractions;

/// <summary>
/// Event publisher interface for publishing domain events
/// </summary>
public interface IEventPublisher
{
    /// <summary>
    /// Publish a domain event to all subscribers
    /// </summary>
    Task Publish<TEvent>(TEvent data, CancellationToken cancellationToken = default) where TEvent : class, IEvent;
}
