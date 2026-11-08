namespace FluentCMS.Events;

/// <summary>A delivery to an outbox handler that ran out of attempts. The event is left out, because it can hold secrets.</summary>
/// <param name="Id">The id of the delivery, which is also its idempotency key.</param>
/// <param name="EventId">The id of the event.</param>
/// <param name="EventType">The full name of the event type.</param>
/// <param name="Handler">The name the handler is registered under.</param>
/// <param name="Attempts">The deliveries it has had.</param>
/// <param name="LastError">The error of the last one, with URL query strings and credentials removed.</param>
/// <param name="OccurredAt">When the event happened.</param>
public sealed record FailedEventDelivery(Guid Id, Guid EventId, string EventType, string Handler, int Attempts, string? LastError, DateTimeOffset OccurredAt);

/// <summary>Lists the outbox deliveries that ran out of attempts, gives them another chance, or removes them.</summary>
public interface IEventDeadLetters
{
    /// <summary>Lists failed deliveries, the most recent first.</summary>
    /// <param name="offset">The number of deliveries to skip.</param>
    /// <param name="limit">The most deliveries to return.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The deliveries.</returns>
    Task<IReadOnlyList<FailedEventDelivery>> ListAsync(int offset, int limit, CancellationToken cancellationToken = default);

    /// <summary>Queues a failed delivery to run again now, with a fresh set of attempts.</summary>
    /// <param name="id">The id of the delivery.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>Whether a failed delivery with this id was queued.</returns>
    Task<bool> RetryAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Deletes a failed delivery for good.</summary>
    /// <param name="id">The id of the delivery.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>Whether a failed delivery with this id was removed.</returns>
    Task<bool> RemoveAsync(Guid id, CancellationToken cancellationToken = default);
}
