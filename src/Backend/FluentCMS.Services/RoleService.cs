namespace FluentCMS.Services;

public interface IRoleService : IAutoRegisterService
{
    Task<IEnumerable<Role>> GetAllForSite(Guid siteId, CancellationToken cancellationToken = default);
    Task<IEnumerable<Role>> GetAll(CancellationToken cancellationToken = default);
    Task<Role> Create(Role role, CancellationToken cancellationToken = default);
    Task<Role> Update(Role role, CancellationToken cancellationToken = default);
    Task<Role> Delete(Guid roleId, CancellationToken cancellationToken = default);
    Task<Role?> GetById(Guid roleId, CancellationToken cancellationToken = default);
}

public class RoleService(IRoleRepository roleRepository, IMessagePublisher messagePublisher) : IRoleService, IMessageHandler<Site>
{
    public async Task<IEnumerable<Role>> GetAllForSite(Guid siteId, CancellationToken cancellationToken = default)
    {
        return await roleRepository.GetAllForSite(siteId, cancellationToken);
    }

    public async Task<IEnumerable<Role>> GetAll(CancellationToken cancellationToken = default)
    {
        return await roleRepository.GetAll(cancellationToken);
    }

    public async Task<Role> Create(Role role, CancellationToken cancellationToken)
    {
        var sameRole = await roleRepository.GetByNameAndSiteId(role.SiteId, role.Name.Trim(), cancellationToken);

        if (sameRole != null)
            throw new AppException(ExceptionCodes.RoleNameIsDuplicated);

        var newRole = await roleRepository.Create(role, cancellationToken) ??
           throw new AppException(ExceptionCodes.RoleUnableToCreate);

        await messagePublisher.Publish(new Message<Role>(ActionNames.RoleCreated, newRole), cancellationToken);

        return newRole;
    }

    public async Task<Role> Update(Role role, CancellationToken cancellationToken)
    {
        var existRole = await roleRepository.GetById(role.Id, cancellationToken) ??
             throw new AppException(ExceptionCodes.RoleNotFound);

        var sameRole = await roleRepository.GetByNameAndSiteId(role.SiteId, role.Name.Trim(), cancellationToken);

        if (sameRole != null && sameRole.Id != role.Id)
            throw new AppException(ExceptionCodes.RoleNameIsDuplicated);

        role.Type = existRole.Type;

        var updatedRole = await roleRepository.Update(role, cancellationToken) ??
            throw new AppException(ExceptionCodes.RoleUnableToUpdate);

        await messagePublisher.Publish(new Message<Role>(ActionNames.RoleUpdated, updatedRole), cancellationToken);

        return updatedRole;
    }

    public async Task<Role> Delete(Guid roleId, CancellationToken cancellationToken = default)
    {
        var existRole = await roleRepository.GetById(roleId, cancellationToken) ??
            throw new AppException(ExceptionCodes.RoleNotFound);

        // check for system roles, they cant be deleted.
        // we need them for system purposes. 
        if (existRole.Type != RoleTypes.UserDefined)
            throw new AppException(ExceptionCodes.RoleCanNotBeDeleted);

        var deletedRole = await roleRepository.Delete(roleId, cancellationToken) ??
            throw new AppException(ExceptionCodes.RoleUnableToDelete);

        await messagePublisher.Publish(new Message<Role>(ActionNames.RoleDeleted, deletedRole), cancellationToken);

        return deletedRole;
    }

    public Task<Role?> GetById(Guid roleId, CancellationToken cancellationToken = default)
    {
        return roleRepository.GetById(roleId, cancellationToken);
    }

    public async Task Handle(Message<Site> message, CancellationToken cancellationToken)
    {
        switch (message.Action)
        {
            case ActionNames.SiteCreated:
                await AddDefaultRolesForSite(message.Payload);
                break;

            case ActionNames.SiteDeleted:
                await DeleteAllRolesOfSite(message.Payload);
                break;
        }
    }

    private async Task DeleteAllRolesOfSite(Site site)
    {
        var siteRoles = await GetAllForSite(site.Id, default);
        await roleRepository.DeleteMany(siteRoles.Select(x => x.Id));
    }

    private async Task AddDefaultRolesForSite(Site site)
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

        await roleRepository.CreateMany(defaultRoles, default);
    }
}
