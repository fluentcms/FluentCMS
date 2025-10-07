namespace FluentCMS.EventBus.Abstractions;

/// <summary>
/// Base class for domain events with common properties
/// </summary>
public abstract class EventBase : IEvent
{
    public DateTimeOffset OccurredAt => DateTimeOffset.UtcNow;
    public Guid EventId => Guid.NewGuid();
}
