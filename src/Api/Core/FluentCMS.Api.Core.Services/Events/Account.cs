namespace FluentCMS.Api.Core.Services.Events;

public class AccountRegisteredEvent(User user) : EventBase
{
    public User User { get; } = user;
}

public class AccountAuthenticatedEvent(User user) : EventBase
{
    public User User { get; } = user;
}

public class AccountChangedPasswordEvent(User user) : EventBase
{
    public User User { get; } = user;
}

public class AccountChangedPasswordByResetTokenEvent(User user) : EventBase
{
    public User User { get; } = user;
}

public class AccountSendResetPasswordTokenEvent(User user) : EventBase
{
    public User User { get; } = user;
}
