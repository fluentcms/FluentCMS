namespace FluentCMS.Infrastructure.Plugins.Lifecycle;

/// <summary>
/// Base class for all plugin system events.
/// Provides default implementations for EventId and OccurredAt.
/// </summary>
public abstract class EventBase : IEvent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EventBase"/> class.
    /// </summary>
    protected EventBase()
    {
        EventId = Guid.NewGuid();
        OccurredAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="EventBase"/> class with a specific event ID.
    /// </summary>
    /// <param name="eventId">The event identifier.</param>
    protected EventBase(Guid eventId)
    {
        EventId = eventId;
        OccurredAt = DateTimeOffset.UtcNow;
    }

    /// <inheritdoc />
    public Guid EventId { get; }

    /// <inheritdoc />
    public DateTimeOffset OccurredAt { get; }
}
