using FluentCMS.Providers.MessageBusProviders;

namespace FluentCMS.Services.MessageHandlers;

public class UserMessageHandler(IUserService userService) : IMessageHandler<SetupTemplate>
{
    public async Task Handle(Message<SetupTemplate> notification, CancellationToken cancellationToken)
    {
        switch (notification.Action)
        {
            case ActionNames.SetupStarted:
                var setupTemplate = notification.Payload;
                var user = new User
                {
                    UserName = setupTemplate.Username,
                    Email = setupTemplate.Email,
                    Enabled = true,
                };

                await userService.Create(user, setupTemplate.Password, cancellationToken);
                break;

            default:
                break;
        }
    }
}
