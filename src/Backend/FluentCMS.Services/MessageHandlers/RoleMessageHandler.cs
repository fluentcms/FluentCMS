using FluentCMS.Providers.MessageBusProviders;

namespace FluentCMS.Services.MessageHandlers;

public class RoleMessageHandler(IRoleService roleService) : IMessageHandler<Site>
{
    public async Task Handle(Message<Site> message, CancellationToken cancellationToken)
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

    private async Task DeleteAllRolesOfSite(Site site, CancellationToken cancellationToken)
    {
        var siteRoles = await roleService.GetAllForSite(site.Id, cancellationToken);
        foreach (var role in siteRoles)
            await roleService.Delete(role.Id, default);
    }

    private async Task AddDefaultRolesForSite(Site site, CancellationToken cancellationToken)
    {
        var defaultRoles = new List<Role>() {
            new() {
                Name="Administrators",
                Description = "Default administrators role with full access to the site",
                Type=RoleTypes.Administrators,
                SiteId=site.Id,
            },
            new() {
                Name="Authenticated Users",
                Description = "All authenticated users (logged in users)",
                Type=RoleTypes.Authenticated,
                SiteId=site.Id,
            },
            new() {
                Name="Guests",
                Description = "Un-authenticated users (not logged in users)",
                Type=RoleTypes.Guest,
                SiteId=site.Id,
            },
            new() {
                Name="All Users",
                Description = "All users (authenticated or un-authenticated users)",
                Type=RoleTypes.AllUsers,
                SiteId=site.Id,
            }
         };

        foreach (var role in defaultRoles)
            await roleService.Create(role, cancellationToken);
    }
}
