namespace FluentCMS.Infrastructure.EventBus.Abstractions;

/// <summary>
/// Marker interface for all domain events
/// </summary>
public interface IEvent
{
    /// <summary>
    /// When the event occurred
    /// </summary>
    DateTimeOffset OccurredAt { get; }

    /// <summary>
    /// Unique event identifier
    /// </summary>
    Guid EventId { get; }
}
