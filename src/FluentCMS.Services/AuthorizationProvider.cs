using FluentCMS.Entities;
using FluentCMS.Repositories;

namespace FluentCMS.Services;

public interface IAuthorizationProvider
{
    bool Authorize(Site site, IEnumerable<string> policyNames);
    bool IsSuperAdmin();
    Task<Permission> Create<T>(T entity, string policyName, CancellationToken cancellationToken = default) where T : IAuthorizeEntity;
}

public class AuthorizationProvider(
    IPermissionRepository permissionRepository,
    IApplicationContext applicationContext,
    IRoleRepository roleRepository) : IAuthorizationProvider
{
    private readonly IEnumerable<Guid> _userRoleIds = applicationContext.Current.RoleIds;
    private IEnumerable<Permission> _permissions = null!;

    private async Task Load(Guid siteId, CancellationToken cancellationToken = default)
    {
        if (_permissions != null)
            return;

        _permissions = await permissionRepository.GetPermissions(siteId, _userRoleIds, cancellationToken);
    }

    public bool IsSuperAdmin()
    {
        // super admins have access to everything
        return applicationContext.Current.IsSuperAdmin;
    }

    public bool Authorize(Site site, IEnumerable<string> policyNames)
    {
        ArgumentNullException.ThrowIfNull(site);
        ArgumentNullException.ThrowIfNull(policyNames);

        // super admins have access to everything
        if (applicationContext.Current.IsSuperAdmin)
            return true;

        Load(site.Id).Wait();

        var entityType = typeof(Site).Name;
        var userRoleIds = applicationContext.Current.RoleIds;

        if (_permissions.Any(x => x.Id == site.Id && x.EntityType == typeof(Site).Name && policyNames.Contains(x.Policy)))
            return true;

        return false;
    }

    public async Task<Permission> Create<T>(T entity, string policyName, CancellationToken cancellationToken = default) where T : IAuthorizeEntity
    {
        ArgumentNullException.ThrowIfNull(entity);

        var siteId = entity.GetType() == typeof(Site) ? entity.Id : entity.SiteId;

        var role = new Role
        {
            SiteId = siteId,
            Name = policyName,
            Description = "Default " + policyName
        };

        _ = await roleRepository.Create(role, cancellationToken) ??
            throw new AppException(ExceptionCodes.RoleUnableToCreate);

        var permission = new Permission
        {
            SiteId = siteId,
            EntityType = typeof(T).Name,
            Policy = policyName,
            EntityId = entity.Id,
            RoleId = role.Id
        };

        return await permissionRepository.Create(permission, cancellationToken) ??
            throw new AppException(ExceptionCodes.PermissionUnableToCreate);
    }
}
