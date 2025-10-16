namespace FluentCMS.Api.Core.Services;

public interface IUserRoleService
{
    Task AssignUserToRole(Guid userId, Guid roleId, CancellationToken cancellationToken = default);
    Task RemoveUserFromRole(Guid userId, Guid roleId, CancellationToken cancellationToken = default);
    Task<List<Role>> GetUserRoles(Guid userId, Guid siteId, CancellationToken cancellationToken = default);
}

internal class UserRoleService(IUserRoleRepository userRoleRepository, IRoleRepository roleRepository, IUserRepository userRepository, IEventPublisher eventPublisher) : IUserRoleService
{
    public async Task AssignUserToRole(Guid userId, Guid roleId, CancellationToken cancellationToken = default)
    {
        var role = await roleRepository.GetById(roleId, cancellationToken);
        var user = await userRepository.GetById(userId, cancellationToken);

        var existingUserRoles = await userRoleRepository.GetUserRoles(userId, role.SiteId, cancellationToken);
        if (existingUserRoles.Any(x => x.RoleId == roleId))
            return;

        var userRole = new UserRole
        {
            RoleId = roleId,
            UserId = userId,
            SiteId = role.SiteId,
        };
        await userRoleRepository.Add(userRole, cancellationToken);

        await eventPublisher.Publish(new UserRoleAddedEvent(userRole), cancellationToken);

    }

    public async Task RemoveUserFromRole(Guid userId, Guid roleId, CancellationToken cancellationToken = default)
    {
        var role = await roleRepository.GetById(roleId, cancellationToken);
        var user = await userRepository.GetById(userId, cancellationToken);

        var existingUserRoles = await userRoleRepository.GetUserRoles(userId, role.SiteId, cancellationToken);
        var exisiting = existingUserRoles.FirstOrDefault(x => x.RoleId == roleId);
        if (exisiting == null)
            return;

        await userRoleRepository.Remove(exisiting, cancellationToken);

        await eventPublisher.Publish(new UserRoleRemovedEvent(exisiting), cancellationToken);
    }

    public async Task<List<Role>> GetUserRoles(Guid userId, Guid siteId, CancellationToken cancellationToken = default)
    {
        var userRoles = await userRoleRepository.GetUserRoles(userId, siteId, cancellationToken) ?? [];
        var roles = await roleRepository.GetAllForSite(siteId, cancellationToken);
        return [.. roles.Where(x => userRoles.Any(ur => ur.RoleId == x.Id))];
    }
}
