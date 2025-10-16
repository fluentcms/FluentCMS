namespace FluentCMS.Api.Plugins.IdentityManagement.Services;

public interface IRoleService
{
    Task<IEnumerable<Role>> GetAllForSite(Guid siteId, CancellationToken cancellationToken = default);
    Task<Role> Add(Role role, CancellationToken cancellationToken = default);
    Task<Role> Update(Role role, CancellationToken cancellationToken = default);
    Task<Role> Remove(Guid roleId, CancellationToken cancellationToken = default);
    Task<Role> GetById(Guid roleId, CancellationToken cancellationToken = default);
}

internal class RoleService(IRoleRepository roleRepository, IEventPublisher eventPublisher, RoleManager<Role> roleManager) : IRoleService
{
    public async Task<IEnumerable<Role>> GetAllForSite(Guid siteId, CancellationToken cancellationToken = default)
    {
        return await roleRepository.GetAllForSite(siteId, cancellationToken);
    }

    public async Task<Role> Add(Role role, CancellationToken cancellationToken)
    {
        var identityResult = await roleManager.CreateAsync(role);
        identityResult.ThrowIfInvalid();

        await eventPublisher.Publish(new RoleAddedEvent(role), cancellationToken);

        return role;
    }

    public async Task<Role> Update(Role role, CancellationToken cancellationToken)
    {
        _ = await roleManager.FindByIdAsync(role.Id.ToString()) ??
            throw new EntityNotFoundException<Role>(role.Id);

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
            throw new EnhancedException(ExceptionCodes.RoleDefaultCanNotBeDeleted);

        var identityResult = await roleManager.DeleteAsync(role);
        identityResult.ThrowIfInvalid();

        await eventPublisher.Publish(new RoleRemovedEvent(role), cancellationToken);

        return role;
    }

    public async Task<Role> GetById(Guid roleId, CancellationToken cancellationToken = default)
    {
        return await roleRepository.GetById(roleId, cancellationToken);
    }
}
