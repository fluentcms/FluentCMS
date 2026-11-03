namespace FluentCMS.Events;

/// <summary>
/// Raises events. Inside a transaction of the content service an event is held back until the outermost commit and
/// never delivered if that rolls back; elsewhere it is delivered at once. A handler that throws is logged and does not
/// affect the caller.
/// </summary>
public interface IEventBus
{
    /// <summary>Raises an event for the handlers of its exact type.</summary>
    /// <typeparam name="TEvent">The type of the event.</typeparam>
    /// <param name="message">The event.</param>
    /// <param name="cancellationToken">Cancels recording the event for the outbox handlers; handlers that run in this process are not cancelled by it.</param>
    /// <returns>A task that completes when the event is delivered, or held back.</returns>
    /// <exception cref="InvalidOperationException">The caller is inside a transaction that the content service does not own, whose end cannot be seen.</exception>
    ValueTask PublishAsync<TEvent>(TEvent message, CancellationToken cancellationToken = default)
        where TEvent : CmsEvent;
}
