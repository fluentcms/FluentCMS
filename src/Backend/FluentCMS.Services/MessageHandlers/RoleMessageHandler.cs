using FluentCMS.Providers.MessageBusProviders;
namespace FluentCMS.Services.MessageHandlers;

public class RoleMessageHandler(IRoleService roleService) : IMessageHandler<SiteTemplate>
{
    public async Task Handle(Message<SiteTemplate> message, CancellationToken cancellationToken)
    {
        switch (message.Action)
        {
            case ActionNames.SiteCreated:
                await AddDefaultRolesForSite(message.Payload, cancellationToken);
                break;

            case ActionNames.SiteDeleted:
                await DeleteAllRolesOfSite(message.Payload, cancellationToken);
                break;
        }
    }

    private async Task DeleteAllRolesOfSite(SiteTemplate siteTemplate, CancellationToken cancellationToken)
    {
        var siteRoles = await roleService.GetAllForSite(siteTemplate.Id, cancellationToken);
        foreach (var role in siteRoles)
            await roleService.Delete(role.Id, default);
    }

    private async Task AddDefaultRolesForSite(SiteTemplate siteTemplate, CancellationToken cancellationToken)
    {
        var defaultRoles = new List<Role>() {
            new() {
                Name="Administrators",
                Description = "Default administrators role with full access to the site",
                Type=RoleTypes.Administrators,
                SiteId=siteTemplate.Id,
            },
            new() {
                Name="Authenticated Users",
                Description = "All authenticated users (logged in users)",
                Type=RoleTypes.Authenticated,
                SiteId=siteTemplate.Id,
            },
            new() {
                Name="Guests",
                Description = "Un-authenticated users (not logged in users)",
                Type=RoleTypes.Guest,
                SiteId=siteTemplate.Id,
            },
            new() {
                Name="All Users",
                Description = "All users (authenticated or un-authenticated users)",
                Type=RoleTypes.AllUsers,
                SiteId=siteTemplate.Id,
            }
         };

        foreach (var role in defaultRoles)
            await roleService.Create(role, cancellationToken);
    }
}
