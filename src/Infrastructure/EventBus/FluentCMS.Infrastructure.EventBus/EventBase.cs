namespace FluentCMS.Infrastructure.EventBus;

/// <summary>
/// Base class for domain events with common properties
/// </summary>
public abstract class EventBase : IEvent
{
    public DateTimeOffset OccurredAt { get; init; }
    public Guid EventId { get; init; }

    protected EventBase()
    {
        OccurredAt = DateTimeOffset.UtcNow;
        EventId = Guid.NewGuid();
    }
}
