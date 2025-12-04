using FluentCMS.Infrastructure.EventBus.Abstractions;
using FluentCMS.Infrastructure.EventBus.Example.Events;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Infrastructure.EventBus.Example.Handlers;

/// <summary>
/// Creates a user profile when a new user registers
/// </summary>
public class CreateUserProfileHandler : IEventSubscriber<UserRegisteredEvent>
{
    private readonly ILogger<CreateUserProfileHandler> _logger;

    public CreateUserProfileHandler(ILogger<CreateUserProfileHandler> logger)
    {
        _logger = logger;
    }

    public async Task Handle(UserRegisteredEvent domainEvent, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Creating user profile for {UserId} - {FullName}", 
            domainEvent.UserId, domainEvent.FullName);
        
        // Simulate creating profile in database
        await Task.Delay(50, cancellationToken);
        
        _logger.LogInformation("User profile created successfully for {UserId}", domainEvent.UserId);
    }
}
