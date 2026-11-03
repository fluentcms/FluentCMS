namespace FluentCMS.Events;

/// <summary>
/// The base of every event. An event is a record that survives a round trip through JSON, because a handler that
/// subscribes through the outbox receives it from the database.
/// </summary>
public abstract record CmsEvent
{
    private readonly DateTimeOffset _created = DateTimeOffset.UtcNow;
    private EventSource? _source;

    /// <summary>Creates an event with no source, which <see cref="IEventBus"/> fills from the current operation when it is published.</summary>
    protected CmsEvent()
    {
    }

    /// <summary>The id of the event, the same for every handler and every delivery of it.</summary>
    public Guid EventId { get; init; } = Guid.CreateVersion7();

    /// <summary>
    /// Where and when the event happened. Set it from an operation with <see cref="EventSource.From"/>, or leave it
    /// for <see cref="IEventBus"/> to set from the operation that publishes the event, or from the system when none is running.
    /// </summary>
    public EventSource Source
    {
        get => _source ?? EventSource.FromSystem(_created);
        init => _source = value;
    }

    internal bool IsAttributed => _source is not null;

    internal void Attribute(EventSource source) => _source = source;
}
