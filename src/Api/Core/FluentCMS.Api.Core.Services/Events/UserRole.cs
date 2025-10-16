namespace FluentCMS.Api.Core.Services.Events;

public class UserRoleAddedEvent(UserRole userRole) : EventBase
{
    public UserRole UserRole { get; } = userRole;
}

public class UserRoleRemovedEvent(UserRole userRole) : EventBase
{
    public UserRole UserRole { get; } = userRole;
}
