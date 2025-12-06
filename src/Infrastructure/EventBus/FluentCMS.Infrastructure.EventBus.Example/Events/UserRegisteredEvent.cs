namespace FluentCMS.Infrastructure.EventBus.Example.Events;

/// <summary>
/// Event raised when a new user registers in the system
/// </summary>
public class UserRegisteredEvent : EventBase
{
    public required string UserId { get; init; }
    public required string Email { get; init; }
    public required string FullName { get; init; }
}
