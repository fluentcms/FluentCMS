namespace FluentCMS.Events;

/// <summary>One delivery of an event to an outbox handler.</summary>
/// <param name="Key">The idempotency key: the same for every delivery of this event to this handler, and different for another event or handler.</param>
/// <param name="Attempt">The number of this delivery, starting at one.</param>
public sealed record EventDelivery(Guid Key, int Attempt);
