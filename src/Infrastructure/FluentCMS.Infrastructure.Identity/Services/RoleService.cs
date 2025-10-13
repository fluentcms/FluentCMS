namespace FluentCMS.Infrastructure.Identity.Services;

public interface IRoleService<TUser, TRole>
    where TUser : UserBase
    where TRole : RoleBase
{
    Task<TRole> Add(TRole role, CancellationToken cancellationToken = default);
    Task Remove(Guid id, CancellationToken cancellationToken = default);
    Task<TRole> Update(TRole role, CancellationToken cancellationToken = default);
    Task<IEnumerable<TRole>> GetAll(CancellationToken cancellationToken = default);
    Task<TRole> GetById(Guid id, CancellationToken cancellationToken = default);
}

public class RoleService<TUser, TRole>(RoleManager<TRole> roleManager) : IRoleService<TUser, TRole>
    where TUser : UserBase
    where TRole : RoleBase
{
    public async Task<TRole> Add(TRole role, CancellationToken cancellationToken = default)
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
        throw new EntityNotFoundException<TRole>(id);

        var result = await roleManager.DeleteAsync(role);

        result.ThrowIfInvalid();
    }

    public async Task<TRole> Update(TRole role, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(role, nameof(role));

        var existingRole = await roleManager.FindByIdAsync(role.Id.ToString()) ??
            throw new EntityNotFoundException<TRole>(role.Id);

        existingRole.Name = role.Name;
        var result = await roleManager.UpdateAsync(existingRole);

        result.ThrowIfInvalid();

        return existingRole;
    }

    public async Task<TRole> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var role = await roleManager.FindByIdAsync(id.ToString()) ??
            throw new EntityNotFoundException<TRole>(id);

        return role;
    }

    public async Task<IEnumerable<TRole>> GetAll(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var roles = await roleManager.Roles.ToListAsync(cancellationToken);
        return roles;
    }

}
