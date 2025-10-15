namespace FluentCMS.Api.Core.Services.Events;

public class RoleAddedEvent(Role role) : EventBase
{
    public Role Role { get; } = role;
}

public class RoleUpdatedEvent(Role role) : EventBase
{
    public Role Role { get; } = role;
}

public class RoleRemovedEvent(Role role) : EventBase
{
    public Role Role { get; } = role;
}
