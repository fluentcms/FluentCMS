namespace FluentCMS.Providers.EventBus.Abstractions;

/// <summary>
/// Base class for domain events with common properties
/// </summary>
public abstract class EventBase : IEvent
{
    public DateTimeOffset OccurredAt => DateTimeOffset.UtcNow;
    public Guid EventId => Guid.NewGuid();
}

public abstract class EventBase<TEntity>(TEntity entity) : EventBase
{
    public TEntity Entity { get; } = entity ??
        throw new ArgumentNullException(nameof(entity));
}
