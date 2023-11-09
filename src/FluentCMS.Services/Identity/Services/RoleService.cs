using FluentCMS.Entities.Identity;
using Microsoft.AspNetCore.Identity;

namespace FluentCMS.Services.Identity;

public class RoleService : IRoleService
{
    protected readonly RoleManager<Role> RoleManager;

    public RoleService(RoleManager<Role> roleManager)
    {
        RoleManager = roleManager;
    }

    public virtual async Task<bool> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var role = await RoleManager.FindByIdAsync(id.ToString());

        if (role == null) return false;

        var idResult = await RoleManager.DeleteAsync(role);

        idResult.ThrowIfInvalid();

        return true;
    }

    public virtual Task<bool> Exists(string roleName, CancellationToken cancellationToken = default)
    {
        return RoleManager.RoleExistsAsync(roleName);
    }

    public virtual Task<IEnumerable<TRole>> GetAll(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // TODO: Refactor this:
        return Task.FromResult(RoleManager.Roles.AsEnumerable());
    }

    public virtual Task<TRole> GetById(TKey id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // TODO: Refactor this:
        return Task.FromResult(RoleManager.Roles.AsEnumerable().Single(r => r.Id.Equals(id)));
    }

    public virtual async Task Create(TRole role, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var idResult = await RoleManager.CreateAsync(role);
        idResult.ThrowIfInvalid();

    }

    public virtual async Task<bool> Update(TRole role, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        
        var idResult = await RoleManager.UpdateAsync(role);
        idResult.ThrowIfInvalid();

        return true;
    }
}