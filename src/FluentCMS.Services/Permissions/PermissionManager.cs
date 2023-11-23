using FluentCMS.Entities;
using FluentCMS.Repositories.Abstractions;

namespace FluentCMS.Services.Permissions;

public class PermissionManager
{
    private readonly IPermissionRepository _permissionRepository;
    private readonly IApplicationContext _applicationContext;
    private readonly IEnumerable<Guid> _userRoleIds;
    private IEnumerable<Permission> _permissions;

    public const string SITE_ADMIN_POLICY = "SiteAdminPolicy";
    public const string SITE_VIEW_POLICY = "SiteViewPolicy";

    public PermissionManager(IPermissionRepository permissionRepository, IApplicationContext applicationContext)
    {
        _permissionRepository = permissionRepository;
        _applicationContext = applicationContext;
        _userRoleIds = _applicationContext.Current.RoleIds;
    }

    private async Task Init(Guid siteId, CancellationToken cancellationToken = default)
    {
        if (_permissions != null)
            return;

        _permissions = await _permissionRepository.GetPermissions(siteId, _userRoleIds, cancellationToken);
    }

    public async Task<bool> HasAccessSiteAdmin(Guid siteId, CancellationToken cancellationToken = default)
    {
        await Init(siteId, cancellationToken);

        if (_applicationContext.Current.IsSuperAdmin)
            return true;

        if (_permissions.Any(x => x.Policy == SITE_ADMIN_POLICY))
            return true;

        return false;
    }

    public async Task<bool> HasAccessSiteView(Guid siteId, CancellationToken cancellationToken = default)
    {
        if (await HasAccessSiteAdmin(siteId, cancellationToken))
            return true;

        if (_permissions.Any(x => x.Policy == SITE_VIEW_POLICY))
            return true;

        return false;
    }

    public async Task<bool> HasAccess(Site site, string policyName, CancellationToken cancellationToken = default) where T : class, IEntity
    {
        if (_applicationContext.Current.IsSuperAdmin)
            return true;

        var entityType = typeof(T).Name;
        var userRoleIds = _applicationContext.Current.RoleIds;
        await _permissionRepository.GetPermissions(entity.Id, userRoleIds, cancellationToken);

        return false;
    }
}
