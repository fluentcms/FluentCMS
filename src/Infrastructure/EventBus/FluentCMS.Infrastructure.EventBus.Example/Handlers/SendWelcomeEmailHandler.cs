using FluentCMS.Infrastructure.EventBus.Abstractions;
using FluentCMS.Infrastructure.EventBus.Example.Events;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Infrastructure.EventBus.Example.Handlers;

/// <summary>
/// Sends a welcome email to newly registered users
/// </summary>
public class SendWelcomeEmailHandler : IEventSubscriber<UserRegisteredEvent>
{
    private readonly ILogger<SendWelcomeEmailHandler> _logger;

    public SendWelcomeEmailHandler(ILogger<SendWelcomeEmailHandler> logger)
    {
        _logger = logger;
    }

    public async Task Handle(UserRegisteredEvent domainEvent, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Sending welcome email to {Email} for user {UserId}", 
            domainEvent.Email, domainEvent.UserId);
        
        // Simulate sending email
        await Task.Delay(100, cancellationToken);
        
        _logger.LogInformation("Welcome email sent successfully to {Email}", domainEvent.Email);
    }
}
