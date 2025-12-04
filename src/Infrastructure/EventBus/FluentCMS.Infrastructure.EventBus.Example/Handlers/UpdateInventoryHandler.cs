using FluentCMS.Infrastructure.EventBus.Abstractions;
using FluentCMS.Infrastructure.EventBus.Example.Events;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Infrastructure.EventBus.Example.Handlers;

/// <summary>
/// Updates inventory when an order is placed
/// </summary>
public class UpdateInventoryHandler : IEventSubscriber<OrderPlacedEvent>
{
    private readonly ILogger<UpdateInventoryHandler> _logger;

    public UpdateInventoryHandler(ILogger<UpdateInventoryHandler> logger)
    {
        _logger = logger;
    }

    public async Task Handle(OrderPlacedEvent domainEvent, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Updating inventory for order {OrderId} with {ItemCount} items", 
            domainEvent.OrderId, domainEvent.ItemCount);
        
        // Simulate inventory update
        await Task.Delay(80, cancellationToken);
        
        _logger.LogInformation("Inventory updated successfully for order {OrderId}", domainEvent.OrderId);
    }
}
