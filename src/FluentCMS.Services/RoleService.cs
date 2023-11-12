using FluentCMS.Entities;
using Microsoft.AspNetCore.Identity;

namespace FluentCMS.Services;

public interface IRoleService
{
    Task<IEnumerable<Role>> GetAll(Guid siteId, CancellationToken cancellationToken = default);
    Task<Role> GetById(Guid id, CancellationToken cancellationToken = default);
    Task<Role> Create(Role role, CancellationToken cancellationToken = default);
    Task<Role> Update(Role role, CancellationToken cancellationToken = default);
    Task Delete(Role role, CancellationToken cancellationToken = default);
}


public class RoleService : BaseService<Role>, IRoleService
{
    protected readonly RoleManager<Role> RoleManager;

    public RoleService(RoleManager<Role> roleManager, IApplicationContext appContext) : base(appContext)
    {
        RoleManager = roleManager;
    }

    public Task<Role> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        var roles = RoleManager.Roles.AsEnumerable().Single(r => r.Id.Equals(id));

        return Task.FromResult(roles);
    }

    public Task<IEnumerable<Role>> GetAll(Guid siteId, CancellationToken cancellationToken = default)
    {
        var roles = RoleManager.Roles.Where(role => role.SiteId.Equals(siteId)).AsEnumerable();

        return Task.FromResult(roles);
    }

    public async Task<Role> Create(Role role, CancellationToken cancellationToken)
    {
        if (role.SiteId != Current.Site.Id)
            throw new Exception("Role must be created for the current site.");

        if (!Current.IsInRole(Current.Site.AdminRoleIds))
            throw new Exception("Only admin can create a role.");

        PrepareForCreate(role);

        var idResult = await RoleManager.CreateAsync(role);

        idResult.ThrowIfInvalid();

        return role;
    }

    public async Task<Role> Update(Role role, CancellationToken cancellationToken)
    {
        if (role.SiteId != Current.Site.Id)
            throw new Exception("Role must be updated for the current site.");

        if (!Current.IsInRole(Current.Site.AdminRoleIds))
            throw new Exception("Only admin can update a role.");

        PrepareForUpdate(role);

        var idResult = await RoleManager.UpdateAsync(role);

        idResult.ThrowIfInvalid();

        return role;
    }

    public async Task Delete(Role role, CancellationToken cancellationToken = default)
    {
        if (role.SiteId != Current.Site.Id)
            throw new Exception("Role must be deleted for the current site.");

        if (!Current.IsInRole(Current.Site.AdminRoleIds))
            throw new Exception("Only admin can update a role.");

        var idResult = await RoleManager.DeleteAsync(role);

        idResult.ThrowIfInvalid();
    }
}