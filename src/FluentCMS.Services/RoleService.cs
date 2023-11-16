using FluentCMS.Entities;
using FluentCMS.Repositories;
using Microsoft.AspNetCore.Identity;

namespace FluentCMS.Services;

public interface IRoleService
{
    Task<IEnumerable<Role>> GetAll(Guid siteId, CancellationToken cancellationToken = default);
    Task<Role> GetById(Guid siteId, Guid id, CancellationToken cancellationToken = default);
    Task<Role> Create(Role role, CancellationToken cancellationToken = default);
    Task<Role> Update(Role role, CancellationToken cancellationToken = default);
    Task Delete(Guid siteId, Guid id, CancellationToken cancellationToken = default);
}

public class RoleService(RoleManager<Role> roleManager, IApplicationContext appContext, ISiteRepository siteRepository) : BaseService<Role>(appContext), IRoleService
{
    private async Task<Site> GetSite(Guid siteId, CancellationToken cancellationToken = default)
    {
        return await siteRepository.GetById(siteId, cancellationToken) ??
            throw new AppException(ExceptionCodes.SiteNotFound);
    }

    public async Task<Role> GetById(Guid siteId, Guid id, CancellationToken cancellationToken = default)
    {
        var siteRoles = await GetAll(siteId, cancellationToken);

        var role = siteRoles.SingleOrDefault(role => role.Id.Equals(id));

        return role ?? throw new AppException(ExceptionCodes.RoleNotFound);
    }

    public async Task<IEnumerable<Role>> GetAll(Guid siteId, CancellationToken cancellationToken = default)
    {
        var site = await GetSite(siteId, cancellationToken);

        // check if current user has admin access to the site
        if (!Current.IsInRole(site.AdminRoleIds))
            throw new AppPermissionException();

        return roleManager.Roles.Where(role => role.SiteId.Equals(siteId)).AsEnumerable();
    }

    public async Task<Role> Create(Role role, CancellationToken cancellationToken)
    {
        var site = await GetSite(role.SiteId, cancellationToken);

        // check if current user has admin access to the site
        if (!Current.IsInRole(site.AdminRoleIds))
            throw new AppPermissionException();

        // check if role name is unique
        var siteRoles = await GetAll(site.Id, cancellationToken);
        if (siteRoles.Any(r => r.Name == role.Name))
            throw new AppException(ExceptionCodes.RoleNameMustBeUnique);

        PrepareForCreate(role);

        var idResult = await roleManager.CreateAsync(role);

        idResult.ThrowIfInvalid();

        return role;
    }

    public async Task<Role> Update(Role role, CancellationToken cancellationToken)
    {
        var site = await GetSite(role.SiteId, cancellationToken);

        // check if current user has admin access to the site
        if (!Current.IsInRole(site.AdminRoleIds))
            throw new AppPermissionException();

        // check if role name is unique
        var siteRoles = await GetAll(site.Id, cancellationToken);
        if (siteRoles.Any(r => r.Name == role.Name && r.Id != role.Id))
            throw new AppException(ExceptionCodes.RoleNameMustBeUnique);

        PrepareForUpdate(role);

        var idResult = await roleManager.UpdateAsync(role);

        idResult.ThrowIfInvalid();

        return role;
    }

    public async Task Delete(Guid siteId, Guid id, CancellationToken cancellationToken = default)
    {
        // permission will be checked inside GetById
        var role = await GetById(siteId, id, cancellationToken);

        var idResult = await roleManager.DeleteAsync(role);

        idResult.ThrowIfInvalid();
    }
}
