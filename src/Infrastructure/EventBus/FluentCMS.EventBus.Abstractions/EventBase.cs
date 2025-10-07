namespace FluentCMS.EventBus.Abstractions;

/// <summary>
/// Base class for domain events with common properties
/// </summary>
public abstract class EventBase : IEvent
{
    public DateTimeOffset OccurredAt { get; }
    public Guid EventId { get; }

    protected EventBase()
    {
        OccurredAt = DateTimeOffset.UtcNow;
        EventId = Guid.NewGuid();
    }
}
