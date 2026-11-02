using System.Diagnostics.CodeAnalysis;

namespace FluentCMS.Events;

/// <summary>
/// Hears events of one type in this process, after the commit of the write that raised them. A handler can be lost
/// to a crash; one that must not be lost implements <see cref="IOutboxEventHandler{TEvent}"/> instead. Register it in the service collection,
/// or with <c>cms.Events.AddHandler</c>. Handlers run in <see cref="Hooks.HookOrderAttribute"/> order.
/// </summary>
/// <typeparam name="TEvent">The type of the event, which is matched exactly.</typeparam>
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "The specification names the contract IEventHandler.")]
public interface IEventHandler<TEvent>
    where TEvent : CmsEvent
{
    /// <summary>Handles the event. A handler that throws is logged and changes nothing about the write or the other handlers.</summary>
    /// <param name="message">The event.</param>
    /// <param name="cancellationToken">Cancelled when the host is stopping.</param>
    /// <returns>A task that completes when the handler is done.</returns>
    ValueTask HandleAsync(TEvent message, CancellationToken cancellationToken);
}
