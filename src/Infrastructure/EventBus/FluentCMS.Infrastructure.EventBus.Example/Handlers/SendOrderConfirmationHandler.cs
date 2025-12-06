using FluentCMS.Infrastructure.EventBus.Abstractions;
using FluentCMS.Infrastructure.EventBus.Example.Events;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Infrastructure.EventBus.Example.Handlers;

/// <summary>
/// Sends order confirmation email to customer
/// </summary>
public class SendOrderConfirmationHandler : IEventSubscriber<OrderPlacedEvent>
{
    private readonly ILogger<SendOrderConfirmationHandler> _logger;

    public SendOrderConfirmationHandler(ILogger<SendOrderConfirmationHandler> logger)
    {
        _logger = logger;
    }

    public async Task Handle(OrderPlacedEvent domainEvent, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Sending order confirmation for order {OrderId} to customer {CustomerId}", 
            domainEvent.OrderId, domainEvent.CustomerId);
        
        // Simulate sending confirmation email
        await Task.Delay(120, cancellationToken);
        
        _logger.LogInformation("Order confirmation sent for order {OrderId} (Total: ${TotalAmount:F2})", 
            domainEvent.OrderId, domainEvent.TotalAmount);
    }
}
