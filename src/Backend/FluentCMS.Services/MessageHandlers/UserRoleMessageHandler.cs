namespace FluentCMS.Services.MessageHandlers;

public class UserRoleMessageHandler(IUserRoleService userRoleService) : IMessageHandler<User>, IMessageHandler<Role>
{
    public async Task Handle(Message<Role> notification, CancellationToken cancellationToken)
    {
        switch (notification.Action)
        {
            case ActionNames.RoleDeleted:
                await userRoleService.DeleteByRoleId(notification.Payload.Id, cancellationToken);
                break;

            default:
                break;
        }
    }

    public async Task Handle(Message<User> notification, CancellationToken cancellationToken)
    {
        switch (notification.Action)
        {
            case ActionNames.UserDeleted:
                await userRoleService.DeleteByUserId(notification.Payload.Id, cancellationToken);
                break;

            default:
                break;
        }
    }
}
