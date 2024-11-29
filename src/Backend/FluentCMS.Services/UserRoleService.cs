﻿namespace FluentCMS.Services;

public interface IUserRoleService : IAutoRegisterService
{
    Task<IEnumerable<Guid>> GetUserRoleIds(Guid userId, Guid siteId, CancellationToken cancellationToken = default);
    Task<IEnumerable<UserRole>> Update(Guid userId, Guid siteId, IEnumerable<Guid> roleIds, CancellationToken cancellationToken = default);
    Task<IEnumerable<UserRole>> GetAllForSite(Guid siteId, CancellationToken cancellationToken = default);
    Task<IEnumerable<UserRole>> DeleteByRoleId(Guid roleId, CancellationToken cancellationToken = default);
    Task<IEnumerable<UserRole>> DeleteByUserId(Guid userId, CancellationToken cancellationToken = default);
}

public class UserRoleService(IUserRoleRepository userRoleRepository, IRoleRepository roleRepository, IApiExecutionContext apiExecutionContext, IMessagePublisher messagePublisher, IPermissionManager permissionManager) : IUserRoleService
{
    public async Task<IEnumerable<UserRole>> DeleteByRoleId(Guid roleId, CancellationToken cancellationToken = default)
    {
        var userRoles = await userRoleRepository.GetByRoleId(roleId, cancellationToken);

        var deleted = await userRoleRepository.DeleteMany(userRoles.Select(x => x.Id), cancellationToken);

        // creating tasks for the message publisher and await them all to finish
        var tasks = deleted?.Select(item => messagePublisher.Publish(new Message<UserRole>(ActionNames.UserRoleDeleted, item!), cancellationToken)).ToList() ?? [];
        await Task.WhenAll(tasks);

        return userRoles;
    }

    public async Task<IEnumerable<UserRole>> DeleteByUserId(Guid userId, CancellationToken cancellationToken = default)
    {
        if (!await permissionManager.HasAccess(GlobalPermissionAction.SuperAdmin, cancellationToken))
            throw new AppException(ExceptionCodes.PermissionDenied);

        var userRoles = await userRoleRepository.GetByUserId(userId, cancellationToken);

        var deleted = await userRoleRepository.DeleteMany(userRoles.Select(x => x.Id), cancellationToken);

        // creating tasks for the message publisher and await them all to finish
        var tasks = deleted?.Select(item => messagePublisher.Publish(new Message<UserRole>(ActionNames.UserRoleDeleted, item!), cancellationToken)).ToList() ?? [];

        await Task.WhenAll(tasks);

        return userRoles;
    }

    public async Task<IEnumerable<UserRole>> GetAllForSite(Guid siteId, CancellationToken cancellationToken = default)
    {
        if (!await permissionManager.HasAccess(siteId, SitePermissionAction.SiteAdmin, cancellationToken))
            throw new AppException(ExceptionCodes.PermissionDenied);

        return await userRoleRepository.GetAllForSite(siteId, cancellationToken);
    }

    public async Task<IEnumerable<Guid>> GetUserRoleIds(Guid userId, Guid siteId, CancellationToken cancellationToken = default)
    {
        // check if the user is the same as the one in the token or site admin
        if (!await permissionManager.HasAccess(siteId, SitePermissionAction.SiteAdmin, cancellationToken) && apiExecutionContext.UserId != userId)
            throw new AppException(ExceptionCodes.PermissionDenied);

        var allRoles = await roleRepository.GetAllForSite(siteId, cancellationToken);
        if (!apiExecutionContext.IsAuthenticated)
            return allRoles.Where(x => x.Type == RoleTypes.Guest || x.Type == RoleTypes.AllUsers).Select(x => x.Id);

        var userRoles = await userRoleRepository.GetUserRoles(userId, siteId, cancellationToken) ?? [];
        var defaultRoles = allRoles.Where(x => x.Type == RoleTypes.Authenticated || x.Type == RoleTypes.AllUsers) ?? [];

        return userRoles.Select(x => x.RoleId).Concat(defaultRoles.Select(x => x.Id)).ToList();
    }

    public async Task<IEnumerable<UserRole>> Update(Guid userId, Guid siteId, IEnumerable<Guid> roleIds, CancellationToken cancellationToken = default)
    {
        if (!await permissionManager.HasAccess(siteId, SitePermissionAction.SiteAdmin, cancellationToken))
            throw new AppException(ExceptionCodes.PermissionDenied);

        // find and remove default roles, just accept user defined roles.
        var allRoles = await roleRepository.GetAllForSite(siteId, cancellationToken);
        var validRoleIds = roleIds.Intersect(allRoles.Where(x => x.Type == RoleTypes.UserDefined || x.Type == RoleTypes.Administrators).Select(x => x.Id));

        // delete all exist UserRoles. 
        var existUserRoles = await userRoleRepository.GetUserRoles(userId, siteId, cancellationToken);
        await userRoleRepository.DeleteMany(existUserRoles.Select(x => x.Id), cancellationToken);

        await messagePublisher.Publish(new Message<IEnumerable<UserRole>>(ActionNames.UserRoleDeleted, existUserRoles), cancellationToken);

        // add all new UserRoles
        var userRoles = validRoleIds.Select(x => new UserRole
        {
            SiteId = siteId,
            RoleId = x,
            UserId = userId,
        });
        var newUserRoles = await userRoleRepository.CreateMany(userRoles, cancellationToken);

        await messagePublisher.Publish(new Message<IEnumerable<UserRole>>(ActionNames.UserRoleCreated, newUserRoles), cancellationToken);

        return newUserRoles;
    }
}
