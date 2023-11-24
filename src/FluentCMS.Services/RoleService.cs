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

public class RoleService(
    IRoleRepository roleRepository,
    IAuthorizationProvider authorizationProvider,
    SitePolicies sitePolicies,
    ISiteRepository siteRepository) : IRoleService
{

    private async Task<Site> GetSite(Guid siteId, CancellationToken cancellationToken = default)
    {
        var site = await siteRepository.GetById(siteId, cancellationToken) ??
            throw new AppException(ExceptionCodes.SiteNotFound);

        // check if current user has admin access to the site
        if (!authorizationProvider.Authorize(site, sitePolicies.Admin))
            throw new AppPermissionException();

        return site;
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
        // permission will be checked inside GetSite
        await GetSite(siteId, cancellationToken);

        return await roleRepository.GetAll(siteId, cancellationToken);
    }

    public async Task<Role> Create(Role role, CancellationToken cancellationToken)
    {
        // permission will be checked inside GetSite
        var site = await GetSite(role.SiteId, cancellationToken);

        // check if role name is unique
        var siteRoles = await GetAll(site.Id, cancellationToken);
        if (siteRoles.Any(r => r.Name.Equals(role.Name, StringComparison.CurrentCultureIgnoreCase)))
            throw new AppException(ExceptionCodes.RoleNameMustBeUnique);

        return await roleRepository.Create(role, cancellationToken) ??
            throw new AppException(ExceptionCodes.RoleUnableToCreate);
    }

    public async Task<Role> Update(Role role, CancellationToken cancellationToken)
    {
        // permission will be checked inside GetSite
        var site = await GetSite(role.SiteId, cancellationToken);

        // check if role name is unique
        var siteRoles = await GetAll(site.Id, cancellationToken);
        if (siteRoles.Any(r => r.Name.Equals(role.Name, StringComparison.CurrentCultureIgnoreCase) && r.Id != role.Id))
            throw new AppException(ExceptionCodes.RoleNameMustBeUnique);

        return await roleRepository.Update(role, cancellationToken) ??
            throw new AppException(ExceptionCodes.RoleUnableToUpdate);
    }

    public async Task Delete(Guid siteId, Guid id, CancellationToken cancellationToken = default)
    {
        // permission will be checked inside GetById
        _ = await GetById(siteId, id, cancellationToken) ??
            throw new AppPermissionException();

        _ = await roleRepository.Delete(id, cancellationToken) ??
            throw new AppException(ExceptionCodes.RoleUnableToDelete);
    }
}
