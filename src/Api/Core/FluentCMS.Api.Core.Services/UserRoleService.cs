namespace FluentCMS.Api.Core.Services;

public interface IUserRoleService
{
    Task AssignUserToRole(Guid userId, Guid roleId, Guid siteId, CancellationToken cancellationToken = default);
    Task RemoveUserFromRole(Guid userId, Guid roleId, Guid siteId, CancellationToken cancellationToken = default);
    Task<List<Role>> GetUserRoles(Guid userId, Guid siteId, CancellationToken cancellationToken = default);
    Task<List<User>> GetUsersInRole(Guid roleId, Guid siteId, CancellationToken cancellationToken = default);
}

//internal class UserRoleService(IUserRoleRepository userRoleRepository, IRoleRepository roleRepository, IApplicationExecutionContext executionContext, IEventPublisher eventPublisher, IPermissionManager permissionManager) : IUserRoleService
//{
//    public async Task<IEnumerable<UserRole>> RemoveRoleForUsers(Guid roleId, CancellationToken cancellationToken = default)
//    {
//        var role = await roleRepository.GetById(roleId, cancellationToken);
//        await permissionManager.CheckSiteAdminPermission(role.SiteId, cancellationToken);

//        var userRoles = await userRoleRepository.GetByRoleId(roleId, cancellationToken);

//        var deleted = await userRoleRepository.RemoveRange(userRoles, cancellationToken);

//        // creating tasks for the message publisher and await them all to finish
//        var tasks = deleted?.Select(item => eventPublisher.Publish(new Message<UserRole>(ActionNames.UserRoleDeleted, item!), cancellationToken)).ToList() ?? [];

//        return userRoles;
//    }

//    public async Task<IEnumerable<UserRole>> RemoveUserRoles(Guid userId, CancellationToken cancellationToken = default)
//    {
//        await permissionManager.CheckSuperAdminPermission(cancellationToken);

//        var userRoles = await userRoleRepository.GetByUserId(userId, cancellationToken);

//        var deleted = await userRoleRepository.RemoveRange(userRoles, cancellationToken);

//        // creating tasks for the message publisher and await them all to finish
//        var tasks = deleted?.Select(item => eventPublisher.Publish(new Message<UserRole>(ActionNames.UserRoleDeleted, item!), cancellationToken)).ToList() ?? [];

//        await Task.WhenAll(tasks);

//        return userRoles;
//    }

//    public async Task<IEnumerable<UserRole>> GetAllForSite(Guid siteId, CancellationToken cancellationToken = default)
//    {
//        await permissionManager.CheckSiteContributorPermission(siteId, cancellationToken);
//        return await userRoleRepository.GetAllForSite(siteId, cancellationToken);
//    }

//    public async Task<IEnumerable<Guid>> GetUserRoleIds(Guid userId, Guid siteId, CancellationToken cancellationToken = default)
//    {
//        // check if the user is the same as the one in the token or site admin
//        if (!await permissionManager.HasAccess(siteId, SitePermissionAction.SiteAdmin, cancellationToken) && executionContext.UserId != userId)
//            throw new EnhancedException(ExceptionCodes.PermissionDenied);

//        var allRoles = await roleRepository.GetAllForSite(siteId, cancellationToken);
//        if (!executionContext.IsAuthenticated)
//            return allRoles.Where(x => x.Type == RoleTypes.Guest || x.Type == RoleTypes.AllUsers).Select(x => x.Id);

//        var userRoles = await userRoleRepository.GetUserRoles(userId, siteId, cancellationToken) ?? [];
//        var defaultRoles = allRoles.Where(x => x.Type == RoleTypes.Authenticated || x.Type == RoleTypes.AllUsers) ?? [];

//        return userRoles.Select(x => x.RoleId).Concat(defaultRoles.Select(x => x.Id)).ToList();
//    }

//    public async Task<IEnumerable<UserRole>> Update(Guid userId, Guid siteId, IEnumerable<Guid> roleIds, CancellationToken cancellationToken = default)
//    {
//        await permissionManager.CheckSiteAdminPermission(siteId, cancellationToken);

//        // find and remove default roles, just accept user defined roles.
//        var allRoles = await roleRepository.GetAllForSite(siteId, cancellationToken);
//        var validRoleIds = roleIds.Intersect(allRoles.Where(x => x.Type == RoleTypes.UserDefined || x.Type == RoleTypes.Administrators).Select(x => x.Id));

//        // delete all exist UserRoles. 
//        var existUserRoles = await userRoleRepository.GetUserRoles(userId, siteId, cancellationToken);
//        await userRoleRepository.DeleteMany(existUserRoles.Select(x => x.Id), cancellationToken);

//        await eventPublisher.Publish(new Message<IEnumerable<UserRole>>(ActionNames.UserRoleDeleted, existUserRoles), cancellationToken);

//        // add all new UserRoles
//        var userRoles = validRoleIds.Select(x => new UserRole
//        {
//            SiteId = siteId,
//            RoleId = x,
//            UserId = userId,
//        });
//        var newUserRoles = await userRoleRepository.CreateMany(userRoles, cancellationToken);

//        await eventPublisher.Publish(new Message<IEnumerable<UserRole>>(ActionNames.UserRoleCreated, newUserRoles), cancellationToken);

//        return newUserRoles;
//    }
//}
//namespace FluentCMS.Api.Core.Services;

//public interface IUserRoleService
//{
//    Task<IEnumerable<Guid>> GetUserRoleIds(Guid userId, Guid siteId, CancellationToken cancellationToken = default);
//    Task<IEnumerable<UserRole>> Update(Guid userId, Guid siteId, IEnumerable<Guid> roleIds, CancellationToken cancellationToken = default);
//    Task<IEnumerable<UserRole>> GetAllForSite(Guid siteId, CancellationToken cancellationToken = default);
//    Task<IEnumerable<UserRole>> RemoveRoleForUsers(Guid roleId, CancellationToken cancellationToken = default);
//    Task<IEnumerable<UserRole>> RemoveUserRoles(Guid userId, CancellationToken cancellationToken = default);
//}

//public class UserRoleService(IUserRoleRepository userRoleRepository, IRoleRepository roleRepository, IApplicationExecutionContext executionContext, IEventPublisher eventPublisher, IPermissionManager permissionManager) : IUserRoleService
//{
//    public async Task<IEnumerable<UserRole>> RemoveRoleForUsers(Guid roleId, CancellationToken cancellationToken = default)
//    {
//        var role = await roleRepository.GetById(roleId, cancellationToken);
//        await permissionManager.CheckSiteAdminPermission(role.SiteId, cancellationToken);

//        var userRoles = await userRoleRepository.GetByRoleId(roleId, cancellationToken);

//        var deleted = await userRoleRepository.RemoveRange(userRoles, cancellationToken);

//        // creating tasks for the message publisher and await them all to finish
//        var tasks = deleted?.Select(item => eventPublisher.Publish(new Message<UserRole>(ActionNames.UserRoleDeleted, item!), cancellationToken)).ToList() ?? [];

//        return userRoles;
//    }

//    public async Task<IEnumerable<UserRole>> RemoveUserRoles(Guid userId, CancellationToken cancellationToken = default)
//    {
//        await permissionManager.CheckSuperAdminPermission(cancellationToken);

//        var userRoles = await userRoleRepository.GetByUserId(userId, cancellationToken);

//        var deleted = await userRoleRepository.RemoveRange(userRoles, cancellationToken);

//        // creating tasks for the message publisher and await them all to finish
//        var tasks = deleted?.Select(item => eventPublisher.Publish(new Message<UserRole>(ActionNames.UserRoleDeleted, item!), cancellationToken)).ToList() ?? [];

//        await Task.WhenAll(tasks);

//        return userRoles;
//    }

//    public async Task<IEnumerable<UserRole>> GetAllForSite(Guid siteId, CancellationToken cancellationToken = default)
//    {
//        await permissionManager.CheckSiteContributorPermission(siteId, cancellationToken);
//        return await userRoleRepository.GetAllForSite(siteId, cancellationToken);
//    }

//    public async Task<IEnumerable<Guid>> GetUserRoleIds(Guid userId, Guid siteId, CancellationToken cancellationToken = default)
//    {
//        // check if the user is the same as the one in the token or site admin
//        if (!await permissionManager.HasAccess(siteId, SitePermissionAction.SiteAdmin, cancellationToken) && executionContext.UserId != userId)
//            throw new EnhancedException(ExceptionCodes.PermissionDenied);

//        var allRoles = await roleRepository.GetAllForSite(siteId, cancellationToken);
//        if (!executionContext.IsAuthenticated)
//            return allRoles.Where(x => x.Type == RoleTypes.Guest || x.Type == RoleTypes.AllUsers).Select(x => x.Id);

//        var userRoles = await userRoleRepository.GetUserRoles(userId, siteId, cancellationToken) ?? [];
//        var defaultRoles = allRoles.Where(x => x.Type == RoleTypes.Authenticated || x.Type == RoleTypes.AllUsers) ?? [];

//        return userRoles.Select(x => x.RoleId).Concat(defaultRoles.Select(x => x.Id)).ToList();
//    }

//    public async Task<IEnumerable<UserRole>> Update(Guid userId, Guid siteId, IEnumerable<Guid> roleIds, CancellationToken cancellationToken = default)
//    {
//        await permissionManager.CheckSiteAdminPermission(siteId, cancellationToken);

//        // find and remove default roles, just accept user defined roles.
//        var allRoles = await roleRepository.GetAllForSite(siteId, cancellationToken);
//        var validRoleIds = roleIds.Intersect(allRoles.Where(x => x.Type == RoleTypes.UserDefined || x.Type == RoleTypes.Administrators).Select(x => x.Id));

//        // delete all exist UserRoles. 
//        var existUserRoles = await userRoleRepository.GetUserRoles(userId, siteId, cancellationToken);
//        await userRoleRepository.DeleteMany(existUserRoles.Select(x => x.Id), cancellationToken);

//        await eventPublisher.Publish(new Message<IEnumerable<UserRole>>(ActionNames.UserRoleDeleted, existUserRoles), cancellationToken);

//        // add all new UserRoles
//        var userRoles = validRoleIds.Select(x => new UserRole
//        {
//            SiteId = siteId,
//            RoleId = x,
//            UserId = userId,
//        });
//        var newUserRoles = await userRoleRepository.CreateMany(userRoles, cancellationToken);

//        await eventPublisher.Publish(new Message<IEnumerable<UserRole>>(ActionNames.UserRoleCreated, newUserRoles), cancellationToken);

//        return newUserRoles;
//    }
//}
