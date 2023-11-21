using FluentCMS.Entities;
using FluentCMS.Repositories;

namespace FluentCMS.Services;

public interface IRoleService
{
    Task<IEnumerable<Role>> GetAll(Guid siteId, CancellationToken cancellationToken = default);
    Task<Role> GetById(Guid siteId, Guid id, CancellationToken cancellationToken = default);
    Task<Role> Create(Role role, CancellationToken cancellationToken = default);
    Task<Role> Update(Role role, CancellationToken cancellationToken = default);
    Task Delete(Guid siteId, Guid id, CancellationToken cancellationToken = default);
}

public class RoleService(IApplicationContext appContext, IRoleRepository roleRepository, ISiteRepository siteRepository) : BaseService<Role>(appContext), IRoleService
{
    private readonly IRoleRepository _roleRepository = roleRepository;

    private async Task<Site> GetSite(Guid siteId, CancellationToken cancellationToken = default)
    {
        return await siteRepository.GetById(siteId, cancellationToken) ??
            throw new AppException(ExceptionCodes.SiteNotFound);
    }

    public async Task<Role> GetById(Guid siteId, Guid id, CancellationToken cancellationToken = default)
    {
        // permission will be checked inside GetAll
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

        return await _roleRepository.GetAll(siteId, cancellationToken);
    }

    public async Task<Role> Create(Role role, CancellationToken cancellationToken)
    {
        var site = await GetSite(role.SiteId, cancellationToken);

        // check if current user has admin access to the site
        if (!Current.IsInRole(site.AdminRoleIds))
            throw new AppPermissionException();

        // check if role name is unique
        var siteRoles = await GetAll(site.Id, cancellationToken);
        if (siteRoles.Any(r => r.Name.ToLower() == role.Name.ToLower()))
            throw new AppException(ExceptionCodes.RoleNameMustBeUnique);

        return await _roleRepository.Create(role, cancellationToken) ??
            throw new AppException(ExceptionCodes.RoleUnableToCreate);
    }

    public async Task<Role> Update(Role role, CancellationToken cancellationToken)
    {
        var site = await GetSite(role.SiteId, cancellationToken);

        // check if current user has admin access to the site
        if (!Current.IsInRole(site.AdminRoleIds))
            throw new AppPermissionException();

        // check if role name is unique
        var siteRoles = await GetAll(site.Id, cancellationToken);
        if (siteRoles.Any(r => r.Name.ToLower() == role.Name.ToLower() && r.Id != role.Id))
            throw new AppException(ExceptionCodes.RoleNameMustBeUnique);

        return await _roleRepository.Update(role, cancellationToken) ??
            throw new AppException(ExceptionCodes.RoleUnableToUpdate);
    }

    public async Task Delete(Guid siteId, Guid id, CancellationToken cancellationToken = default)
    {
        // permission will be checked inside GetById
        var role = await GetById(siteId, id, cancellationToken);
        if (role == null)
            throw new AppPermissionException();

        var deletedRole = await _roleRepository.Delete(id, cancellationToken);
        if (deletedRole == null)
            throw new AppException(ExceptionCodes.RoleUnableToDelete);
    }
}
