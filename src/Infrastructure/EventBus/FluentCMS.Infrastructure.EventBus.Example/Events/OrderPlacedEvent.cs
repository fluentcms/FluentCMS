namespace FluentCMS.Infrastructure.EventBus.Example.Events;

/// <summary>
/// Event raised when a customer places an order
/// </summary>
public class OrderPlacedEvent : EventBase
{
    public required string OrderId { get; init; }
    public required string CustomerId { get; init; }
    public required decimal TotalAmount { get; init; }
    public required int ItemCount { get; init; }
}
