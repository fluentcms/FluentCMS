namespace FluentCMS.Infrastructure.Plugins.Lifecycle;

/// <summary>
/// Base interface for all plugin system events.
/// All events should be immutable and contain metadata for tracing and correlation.
/// </summary>
public interface IEvent
{
    /// <summary>
    /// Gets the unique identifier for this event instance.
    /// </summary>
    Guid EventId { get; }

    /// <summary>
    /// Gets when this event occurred.
    /// </summary>
    DateTimeOffset OccurredAt { get; }
}
