namespace FluentCMS.Events;

/// <summary>
/// The base of every event. An event is a record that survives a round trip through JSON, because a handler that
/// subscribes through the outbox receives it from the database.
/// </summary>
public abstract record CmsEvent
{
    /// <summary>Creates an event whose source is the system, now.</summary>
    protected CmsEvent()
    {
    }

    /// <summary>The id of the event, the same for every handler and every delivery of it.</summary>
    public Guid EventId { get; init; } = Guid.CreateVersion7();

    /// <summary>Where and when the event happened. Set it from the operation with <see cref="EventSource.From"/>.</summary>
    public EventSource Source { get; init; } = EventSource.FromSystem(DateTimeOffset.UtcNow);
}
