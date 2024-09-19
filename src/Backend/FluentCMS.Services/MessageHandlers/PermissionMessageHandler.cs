namespace FluentCMS.Services.MessageHandlers;

public class PermissionMessageHandler(IPermissionService permissionService) : IMessageHandler<SiteTemplate>
{
    public async Task Handle(Message<SiteTemplate> notification, CancellationToken cancellationToken)
    {
        switch (notification.Action)
        {
            case ActionNames.SiteCreated:
                var site = notification.Payload;
                var roles = notification.Payload.Roles;
                var adminRoles = roles.Where(r => site.AdminRoles.Contains(r.Name)).Select(r => r.Id).ToList();
                var contributorRoles = roles.Where(r => site.ContributorRoles.Contains(r.Name)).Select(r => r.Id).ToList();
                await permissionService.Set(site.Id, SitePermissionAction.SiteAdmin, adminRoles, cancellationToken);
                await permissionService.Set(site.Id, SitePermissionAction.SiteContributor, contributorRoles, cancellationToken);

                break;

            case ActionNames.SiteDeleted:
                break;
        }
    }
}
