using FluentCMS.Api.Core.Services.InternalTemp;

namespace FluentCMS.Api.Core.Services;

public interface IRoleService 
{
    Task<IEnumerable<Role>> GetAllForSite(Guid siteId, CancellationToken cancellationToken = default);
    Task<Role> Add(Role role, CancellationToken cancellationToken = default);
    Task<Role> Update(Role role, CancellationToken cancellationToken = default);
    Task<Role> Remove(Guid roleId, CancellationToken cancellationToken = default);
    Task<Role> GetById(Guid roleId, CancellationToken cancellationToken = default);
}

public class RoleService(IRoleRepository roleRepository, IEventPublisher messagePublisher, IPermissionManager permissionManager) : IRoleService
{
    public async Task<IEnumerable<Role>> GetAllForSite(Guid siteId, CancellationToken cancellationToken = default)
    {
        // no need to check for permission 
        return await roleRepository.GetAllForSite(siteId, cancellationToken);
    }

    public async Task<Role> Add(Role role, CancellationToken cancellationToken)
    {
        if (!await permissionManager.HasAccess(role.SiteId, SitePermissionAction.SiteAdmin, cancellationToken))
            throw new EnhancedException(ExceptionCodes.PermissionDenied);

        // check for duplicated role name
        var allRoles = await roleRepository.GetAllForSite(role.SiteId, cancellationToken);
        if (allRoles.Where(r => r.Name.Equals(role.Name, StringComparison.CurrentCultureIgnoreCase)).Any())
            throw new EnhancedException(ExceptionCodes.RoleNameShouldBeUnique);

        var newRole = await roleRepository.Add(role, cancellationToken) ??
           throw new EnhancedException(ExceptionCodes.RoleUnableToCreate);

        await messagePublisher.Publish(new Message<Role>(ActionNames.RoleCreated, newRole), cancellationToken);

        return newRole;
    }

    public async Task<Role> Update(Role role, CancellationToken cancellationToken)
    {
        var existingRole = await roleRepository.GetById(role.Id, cancellationToken) ??
            throw new EnhancedException(ExceptionCodes.RoleNotFound);

        // role type can't be changed after creation
        role.Type = existingRole.Type;

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

        var updatedRole = await roleRepository.Update(role, cancellationToken) ??
            throw new EnhancedException(ExceptionCodes.RoleUnableToUpdate);

        await messagePublisher.Publish(new Message<Role>(ActionNames.RoleUpdated, updatedRole), cancellationToken);

        return updatedRole;
    }

    public async Task<Role> Remove(Guid roleId, CancellationToken cancellationToken = default)
    {
        var existingRole = await roleRepository.GetById(roleId, cancellationToken) ??
            throw new EnhancedException(ExceptionCodes.RoleNotFound);

        if (!await permissionManager.HasAccess(existingRole.SiteId, SitePermissionAction.SiteAdmin, cancellationToken))
            throw new EnhancedException(ExceptionCodes.PermissionDenied);

        // Only user defined roles can be deleted
        if (existingRole.Type != RoleTypes.UserDefined)
            throw new EnhancedException(ExceptionCodes.RoleCanNotBeDeleted);

        var deletedRole = await roleRepository.Remove(roleId, cancellationToken) ??
            throw new EnhancedException(ExceptionCodes.RoleUnableToDelete);

        await messagePublisher.Publish(new Message<Role>(ActionNames.RoleDeleted, deletedRole), cancellationToken);

        return deletedRole;
    }

    public async Task<Role> GetById(Guid roleId, CancellationToken cancellationToken = default)
    {
        // no need to check for permission
        return await roleRepository.GetById(roleId, cancellationToken) ??
            throw new EnhancedException(ExceptionCodes.RoleNotFound);
    }
}
