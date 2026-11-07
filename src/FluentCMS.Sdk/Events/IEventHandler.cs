using System.Diagnostics.CodeAnalysis;

namespace FluentCMS.Events;

/// <summary>
/// Hears events of one type in this process, after the commit of the write that raised them. A handler can be lost
/// to a crash; one that must not be lost implements <see cref="IOutboxEventHandler{TEvent}"/> instead. Register it in the service collection,
/// or with <c>cms.Events.AddHandler</c>. Handlers run in <see cref="Hooks.HookOrderAttribute"/> order. The caller of the write
/// waits for each one up to the host's timeout (<c>FluentCms:Events:Handlers:Timeout</c>, five seconds by default); one that
/// takes longer has its token cancelled and is left to end on its own, so slow work belongs in an outbox handler or a job.
/// </summary>
/// <typeparam name="TEvent">The type of the event, which is matched exactly.</typeparam>
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "The specification names the contract IEventHandler.")]
public interface IEventHandler<TEvent>
    where TEvent : CmsEvent
{
    /// <summary>Handles the event. A handler that throws is logged and changes nothing about the write or the other handlers.</summary>
    /// <param name="message">The event.</param>
    /// <param name="cancellationToken">Cancelled when the host is stopping or the handler outlives the timeout, never when the request that raised the event ends.</param>
    /// <returns>A task that completes when the handler is done.</returns>
    ValueTask HandleAsync(TEvent message, CancellationToken cancellationToken);
}
