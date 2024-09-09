using FluentCMS.Providers.MessageBusProviders;

namespace FluentCMS.Services;

public interface IRoleService : IAutoRegisterService
{
    Task<IEnumerable<Role>> GetAllForSite(Guid siteId, CancellationToken cancellationToken = default);
    Task<Role> Create(Role role, CancellationToken cancellationToken = default);
    Task<Role> Update(Role role, CancellationToken cancellationToken = default);
    Task<Role> Delete(Guid roleId, CancellationToken cancellationToken = default);
    Task<Role?> GetById(Guid roleId, CancellationToken cancellationToken = default);
}

public class RoleService(IRoleRepository roleRepository, IMessagePublisher messagePublisher, IPermissionManager permissionManager, ISiteRepository siteRepository) : IRoleService
{
    public async Task<IEnumerable<Role>> GetAllForSite(Guid siteId, CancellationToken cancellationToken = default)
    {
        return await roleRepository.GetAllForSite(siteId, cancellationToken);
    }

    public async Task<Role> Create(Role role, CancellationToken cancellationToken)
    {
        var site = await siteRepository.GetById(role.SiteId, cancellationToken) ??
            throw new AppException(ExceptionCodes.SiteNotFound);

        //if (!await permissionManager.HasAccess(site, PermissionActionNames.SiteAdmin, cancellationToken))
        //    throw new AppException(ExceptionCodes.PermissionDenied);

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

        //if (!await permissionManager.HasAccess(role, PermissionActionNames.SiteAdmin, cancellationToken))
        //    throw new AppException(ExceptionCodes.PermissionDenied);

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
        var existingRole = await roleRepository.GetById(roleId, cancellationToken) ??
            throw new AppException(ExceptionCodes.RoleNotFound);

        //if (!await permissionManager.HasAccess(existingRole, PermissionActionNames.SiteAdmin, cancellationToken))
        //    throw new AppException(ExceptionCodes.PermissionDenied);

        // check for system roles, they cant be deleted.
        // we need them for system purposes. 
        if (existingRole.Type != RoleTypes.UserDefined)
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
}
