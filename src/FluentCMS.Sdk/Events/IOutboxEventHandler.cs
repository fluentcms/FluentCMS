using System.Diagnostics.CodeAnalysis;

namespace FluentCMS.Events;

/// <summary>
/// Hears events of one type and never misses one. The event is recorded in the transaction that raised it and a job
/// delivers it, so a handler runs after a crash too, at least once and possibly more than once: use
/// <see cref="EventDelivery.Key"/> to ignore a repeat. Register it with <c>cms.Events.AddOutboxHandler</c>.
/// Deliveries start in <see cref="Hooks.HookOrderAttribute"/> order and in the order the events were recorded, but a failed delivery is retried later and instances deliver at the same time, so do not depend on order.
/// </summary>
/// <typeparam name="TEvent">The type of the event, which is matched exactly.</typeparam>
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "It is the outbox counterpart of IEventHandler.")]
public interface IOutboxEventHandler<TEvent>
    where TEvent : CmsEvent
{
    /// <summary>Handles the event. A handler that throws is called again later with backoff, until it is out of attempts.</summary>
    /// <param name="message">The event.</param>
    /// <param name="delivery">Which delivery this is.</param>
    /// <param name="cancellationToken">Cancelled when the host is stopping and when the delivery has run out of time; a delivery that outlasts the time limit is abandoned and counted as failed.</param>
    /// <returns>A task that completes when the handler is done.</returns>
    ValueTask HandleAsync(TEvent message, EventDelivery delivery, CancellationToken cancellationToken);
}
