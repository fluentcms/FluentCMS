using FluentCMS.Content;
using FluentCMS.Events;

namespace FluentCMS.Core.Events;

internal sealed class EventBus(EventBuffer buffer, OutboxWriter outbox, ITransactionProbe transaction, IOperationContextAccessor operation, TimeProvider time) : IEventBus
{
    public async ValueTask PublishAsync<TEvent>(TEvent message, CancellationToken cancellationToken = default)
        where TEvent : CmsEvent
    {
        ArgumentNullException.ThrowIfNull(message);

        if (!message.IsAttributed)
        {
            message.Attribute(operation.Current is { } current ? EventSource.From(current) : EventSource.FromSystem(time.GetUtcNow()));
        }

        if (buffer.IsOpen)
        {
            buffer.Add(message);
            return;
        }

        // The end of a transaction someone else began cannot be seen, and an event that may yet roll back is never delivered.
        if (transaction.InTransaction)
        {
            throw new InvalidOperationException(
                $"{EventErrorCodes.PublishInForeignTransaction}: an event cannot be published inside a transaction the content service does not own, because its end cannot be seen; publish it after the transaction.");
        }

        await outbox.RecordAsync([message], cancellationToken);
        await buffer.DispatchNowAsync(message);
    }
}

// The content service owns the transactions events can wait for; this says whether another one is open.
internal interface ITransactionProbe
{
    bool InTransaction { get; }
}
