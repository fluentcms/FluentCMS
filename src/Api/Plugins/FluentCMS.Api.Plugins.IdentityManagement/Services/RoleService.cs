namespace FluentCMS.Api.Plugins.IdentityManagement.Services;

public interface IRoleService
{
    Task<IEnumerable<Role>> GetAll(CancellationToken cancellationToken = default);
    Task<Role> Add(Role role, CancellationToken cancellationToken = default);
    Task<Role> Update(Role role, CancellationToken cancellationToken = default);
    Task<Role> Remove(Guid roleId, CancellationToken cancellationToken = default);
    Task<Role> GetById(Guid roleId, CancellationToken cancellationToken = default);
    Task<IEnumerable<Role>> GetUserRoles(Guid userId, CancellationToken cancellationToken = default);
    Task UpdateUserRoles(Guid userId, IEnumerable<Guid> roleIds, CancellationToken cancellationToken = default);
}

internal class RoleService(ISecurityContext securityContext, IRoleRepository roleRepository, IUserRoleRepository userRoleRepository, IEventPublisher eventPublisher, RoleManager<Role> roleManager) : IRoleService
{
    public async Task<IEnumerable<Role>> GetAll(CancellationToken cancellationToken = default)
    {
        return await roleRepository.GetAllForSite(securityContext.GetSiteId(), cancellationToken);
    }

    public async Task<Role> Add(Role role, CancellationToken cancellationToken)
    {
        if (role.Type != RoleTypes.UserDefined)
            throw new EnhancedException(MessageCodes.RoleInvalidTypeForCreation);

        ValidateSiteContext(role);

        var identityResult = await roleManager.CreateAsync(role);
        identityResult.ThrowIfInvalid();

        await eventPublisher.Publish(new RoleAddedEvent(role), cancellationToken);

        return role;
    }

    public async Task<Role> Update(Role role, CancellationToken cancellationToken)
    {
        if (role.Type != RoleTypes.UserDefined)
            throw new EnhancedException(MessageCodes.RoleInvalidTypeForCreation);

        ValidateSiteContext(role);

        var existing = await roleManager.FindByIdAsync(role.Id.ToString()) ??
             throw new EntityNotFoundException<Role>(role.Id);

        ValidateSiteContext(existing);

        var identityResult = await roleManager.UpdateAsync(role);
        identityResult.ThrowIfInvalid();

        await eventPublisher.Publish(new RoleUpdatedEvent(role), cancellationToken);
        return role;
    }

    public async Task<Role> Remove(Guid roleId, CancellationToken cancellationToken = default)
    {
        var role = await roleManager.FindByIdAsync(roleId.ToString()) ??
            throw new EntityNotFoundException<Role>(roleId);

        if (role.Type != RoleTypes.UserDefined)
            throw new EnhancedException(MessageCodes.RoleDefaultCanNotBeDeleted);

        ValidateSiteContext(role);

        var identityResult = await roleManager.DeleteAsync(role);
        identityResult.ThrowIfInvalid();

        await eventPublisher.Publish(new RoleRemovedEvent(role), cancellationToken);

        return role;
    }

    public async Task<Role> GetById(Guid roleId, CancellationToken cancellationToken = default)
    {
        var role = await roleRepository.GetById(roleId, cancellationToken);
        ValidateSiteContext(role);
        return role;
    }

    public async Task<IEnumerable<Role>> GetUserRoles(Guid userId, CancellationToken cancellationToken = default)
    {
        var userRoles = await userRoleRepository.GetUserRoles(userId, securityContext.GetSiteId(), cancellationToken);
        var roles = await roleRepository.GetByIds(userRoles.Select(ur => ur.RoleId), cancellationToken);
        return roles;
    }

    public async Task UpdateUserRoles(Guid userId, IEnumerable<Guid> roleIds, CancellationToken cancellationToken = default)
    {
        var existingUserRoles = await userRoleRepository.GetUserRoles(userId, securityContext.GetSiteId(), cancellationToken);
        var roleIdsToRemove = existingUserRoles.Where(r => !roleIds.Contains(r.RoleId));
        var roleIdsToAdd = roleIds.Where(r => !existingUserRoles.Any(er => er.RoleId == r));

        await userRoleRepository.RemoveRange(roleIdsToRemove, cancellationToken);
        await userRoleRepository.AddRange(roleIdsToAdd.Select(rid => new UserRole
        {
            UserId = userId,
            RoleId = rid,
            SiteId = securityContext.GetSiteId()
        }), cancellationToken);
    }

    private void ValidateSiteContext(Role role)
    {
        if (role.SiteId != securityContext.GetSiteId())
            throw new EnhancedException(MessageCodes.RoleSiteIdMismatch);
    }
}
