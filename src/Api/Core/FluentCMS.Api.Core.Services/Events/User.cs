namespace FluentCMS.Api.Core.Services.Events;

public class UserAddedEvent(User user) : EventBase
{
    public User User { get; } = user;
}

public class UserUpdatedEvent(User user) : EventBase
{
    public User User { get; } = user;
}

public class UserRemovedEvent(User user) : EventBase
{
    public User User { get; } = user;
}

public class UserChangedPasswordEvent(User user) : EventBase
{
    public User User { get; } = user;
}
