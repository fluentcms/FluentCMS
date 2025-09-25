namespace FluentCMS.Plugins.IdentityManager.Services;

public interface IRoleService
{
    Task<Role> Add(Role role, CancellationToken cancellationToken = default);
    Task Remove(Guid id, CancellationToken cancellationToken = default);
    Task<Role> Update(Role role, CancellationToken cancellationToken = default);
    Task<IEnumerable<Role>> GetAll(CancellationToken cancellationToken = default);
    Task<Role> GetById(Guid id, CancellationToken cancellationToken = default);
}

public class RoleService(RoleManager<Role> roleManager) : IRoleService
{
    public async Task<Role> Add(Role role, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(role, nameof(role));

        var result = await roleManager.CreateAsync(role);

        result.ThrowIfInvalid();

        return role;
    }

    public async Task Remove(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var role = await roleManager.FindByIdAsync(id.ToString()) ??
        throw new EntityNotFoundException<Role>(id);

        var result = await roleManager.DeleteAsync(role);

        result.ThrowIfInvalid();
    }

    public async Task<Role> Update(Role role, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(role, nameof(role));

        var existingRole = await roleManager.FindByIdAsync(role.Id.ToString()) ??
            throw new EntityNotFoundException<Role>(role.Id);

        existingRole.Name = role.Name;
        var result = await roleManager.UpdateAsync(existingRole);

        result.ThrowIfInvalid();

        return existingRole;
    }

    public async Task<Role> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var role = await roleManager.FindByIdAsync(id.ToString()) ??
            throw new EntityNotFoundException<Role>(id);

        return role;
    }

    public async Task<IEnumerable<Role>> GetAll(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var roles = await roleManager.Roles.ToListAsync(cancellationToken);
        return roles;
    }

}
