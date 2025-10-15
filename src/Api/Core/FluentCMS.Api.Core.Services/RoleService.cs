namespace FluentCMS.Api.Core.Services;

public interface IRoleService
{
    Task<IEnumerable<Role>> GetAllForSite(Guid siteId, CancellationToken cancellationToken = default);
    Task<Role> Add(Role role, CancellationToken cancellationToken = default);
    Task<Role> Update(Role role, CancellationToken cancellationToken = default);
    Task<Role> Remove(Guid roleId, CancellationToken cancellationToken = default);
    Task<Role> GetById(Guid roleId, CancellationToken cancellationToken = default);
}

public class RoleService(IRoleRepository roleRepository, IEventPublisher eventPublisher, IPermissionManager permissionManager) : IRoleService
{
    public async Task<IEnumerable<Role>> GetAllForSite(Guid siteId, CancellationToken cancellationToken = default)
    {
        await permissionManager.CheckSiteContributorPermission(siteId, cancellationToken);

        return await roleRepository.GetAllForSite(siteId, cancellationToken);
    }

    public async Task<Role> Add(Role role, CancellationToken cancellationToken)
    {
        await permissionManager.CheckSiteAdminPermission(role.SiteId, cancellationToken);

        // check for duplicated role name
        var allRoles = await roleRepository.GetAllForSite(role.SiteId, cancellationToken);
        if (allRoles.Where(r => r.Name!.Equals(role.Name, StringComparison.CurrentCultureIgnoreCase)).Any())
            throw new EnhancedException(ExceptionCodes.RoleNameShouldBeUnique);

        await roleRepository.Add(role, cancellationToken);

        await eventPublisher.Publish(new Message<Role>(ActionNames.RoleCreated, role), cancellationToken);

        return role;
    }

    public async Task<Role> Update(Role role, CancellationToken cancellationToken)
    {
        await permissionManager.CheckSiteAdminPermission(role.SiteId, cancellationToken);

        var existingRole = await roleRepository.GetById(role.Id, cancellationToken);

        // role type can't be changed after creation
        if (role.Type != existingRole.Type)
            throw new EnhancedException(ExceptionCodes.RoleTypeCanNotBeChanged);

        var siteId = existingRole.SiteId;

        if (!await permissionManager.HasAccess(siteId, SitePermissionAction.SiteAdmin, cancellationToken))
            throw new EnhancedException(ExceptionCodes.PermissionDenied);

        // Check if role name is changed
        if (!existingRole.Name.Equals(role.Name, StringComparison.CurrentCultureIgnoreCase))
        {
            // Check for duplicated role name 
            var allRoles = await roleRepository.GetAllForSite(siteId, cancellationToken);
            if (allRoles.Where(r => r.Name.Equals(role.Name, StringComparison.CurrentCultureIgnoreCase)).Any())
                throw new EnhancedException(ExceptionCodes.RoleNameShouldBeUnique);
        }

        await roleRepository.Update(role, cancellationToken);

        await eventPublisher.Publish(new Message<Role>(ActionNames.RoleUpdated, role), cancellationToken);

        return role;
    }

    public async Task<Role> Remove(Guid roleId, CancellationToken cancellationToken = default)
    {
        var role = await roleRepository.GetById(roleId, cancellationToken);

        await permissionManager.CheckSiteAdminPermission(role.SiteId, cancellationToken);

        // Only user defined roles can be deleted
        if (role.Type != RoleTypes.UserDefined)
            throw new EnhancedException(ExceptionCodes.RoleDefaultCanNotBeDeleted);

        await roleRepository.Remove(roleId, cancellationToken);

        await eventPublisher.Publish(new Message<Role>(ActionNames.RoleDeleted, role), cancellationToken);

        return role;
    }

    public async Task<Role> GetById(Guid roleId, CancellationToken cancellationToken = default)
    {
        var role = await roleRepository.GetById(roleId, cancellationToken);

        await permissionManager.CheckSiteContributorPermission(role.SiteId, cancellationToken);

        return role;
    }
}
