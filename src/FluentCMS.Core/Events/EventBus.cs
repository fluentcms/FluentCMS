using FluentCMS.Events;

namespace FluentCMS.Core.Events;

internal sealed class EventBus(EventBuffer buffer, EventDispatcher dispatcher, OutboxWriter outbox, ITransactionProbe transaction) : IEventBus
{
    public async ValueTask PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : CmsEvent
    {
        ArgumentNullException.ThrowIfNull(@event);

        if (buffer.IsOpen)
        {
            buffer.Add(@event);
            return;
        }

        // The end of a transaction someone else began cannot be seen, and an event that may yet roll back is never delivered.
        if (transaction.InTransaction)
        {
            throw new InvalidOperationException(
                $"{EventErrorCodes.PublishInForeignTransaction}: an event cannot be published inside a transaction the content service does not own, because its end cannot be seen; publish it after the transaction.");
        }

        await outbox.RecordAsync([@event], cancellationToken);
        await dispatcher.DispatchAsync(@event);
        await outbox.KickIfStagedAsync();
    }
}

// The content service owns the transactions events can wait for; this says whether another one is open.
internal interface ITransactionProbe
{
    bool InTransaction { get; }
}
