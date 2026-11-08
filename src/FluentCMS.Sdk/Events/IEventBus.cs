namespace FluentCMS.Events;

/// <summary>
/// Raises events. Inside a transaction of the content service an event is held back until the outermost commit and
/// never delivered if that rolls back; outside any transaction it is delivered at once. Inside any other open transaction,
/// on a context of core or on a plugin's <c>PluginDbContext&lt;TPlugin&gt;</c>, the publish is refused with <c>FCMS2602</c>,
/// because the end of that transaction cannot be seen: a plugin publishes after its own transaction commits. A handler that
/// throws is logged and does not affect the caller.
/// </summary>
public interface IEventBus
{
    /// <summary>Raises an event for the handlers of its exact type.</summary>
    /// <typeparam name="TEvent">The type of the event.</typeparam>
    /// <param name="message">The event.</param>
    /// <param name="cancellationToken">Cancels recording the event for the outbox handlers; handlers that run in this process are not cancelled by it.</param>
    /// <returns>A task that completes when the event is delivered, or held back.</returns>
    /// <exception cref="InvalidOperationException">The caller is inside a transaction that the content service does not own, whose end cannot be seen, such as one on a plugin's context (<c>FCMS2602</c>).</exception>
    ValueTask PublishAsync<TEvent>(TEvent message, CancellationToken cancellationToken = default)
        where TEvent : CmsEvent;
}
