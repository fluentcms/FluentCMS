namespace FluentCMS.Providers.EventBus.Abstractions;

/// <summary>
/// Base class for domain events with common properties
/// </summary>
public abstract class EventBase : IEvent
{
    protected EventBase()
    {
        EventId = Guid.NewGuid();
        OccurredAt = DateTime.UtcNow;
    }

    public DateTime OccurredAt { get; }
    public Guid EventId { get; }
}

public abstract class EventBase<TEntity>(TEntity entity) : EventBase
{
    public TEntity Entity { get; } = entity ??
        throw new ArgumentNullException(nameof(entity));
}
