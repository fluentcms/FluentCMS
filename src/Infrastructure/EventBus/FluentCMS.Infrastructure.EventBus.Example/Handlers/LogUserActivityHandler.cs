using FluentCMS.Infrastructure.EventBus.Abstractions;
using FluentCMS.Infrastructure.EventBus.Example.Events;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Infrastructure.EventBus.Example.Handlers;

/// <summary>
/// Logs user activity for audit purposes
/// </summary>
public class LogUserActivityHandler : IEventSubscriber<UserRegisteredEvent>
{
    private readonly ILogger<LogUserActivityHandler> _logger;

    public LogUserActivityHandler(ILogger<LogUserActivityHandler> logger)
    {
        _logger = logger;
    }

    public async Task Handle(UserRegisteredEvent domainEvent, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Logging user activity for {UserId}", domainEvent.UserId);
        
        // Simulate logging to audit database
        await Task.Delay(30, cancellationToken);
        
        _logger.LogInformation("Activity logged: User {UserId} registered at {OccurredAt}", 
            domainEvent.UserId, domainEvent.OccurredAt);
    }
}
