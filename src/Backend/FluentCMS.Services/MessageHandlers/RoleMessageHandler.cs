namespace FluentCMS.Services.MessageHandlers;

public class RoleMessageHandler(IRoleService roleService) : IMessageHandler<SiteTemplate>
{
    public async Task Handle(Message<SiteTemplate> notification, CancellationToken cancellationToken)
    {
        switch (notification.Action)
        {
            case ActionNames.SiteCreated:
                foreach (var role in notification.Payload.Roles)
                    await roleService.Create(role, cancellationToken);
                break;

            default:
                break;
        }
    }
}
