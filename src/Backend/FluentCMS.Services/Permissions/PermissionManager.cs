namespace FluentCMS.Services.Permissions;

public class PermissionManager : IPermissionManager
{
    private readonly IApiExecutionContext _apiExecutionContext;
    private readonly IPermissionRepository _permissionRepository;
    private readonly IUserRoleRepository _userRoleRepository;
    private readonly IGlobalSettingsRepository _globalSettingsRepository;
    private readonly bool _isSuperAdmin = false;
    private readonly List<UserRole> _userRoles;

    public PermissionManager(IApiExecutionContext apiExecutionContext, IPermissionRepository permissionRepository, IUserRoleRepository userRoleRepository, IGlobalSettingsRepository globalSettingsRepository)
    {
        _apiExecutionContext = apiExecutionContext;
        _permissionRepository = permissionRepository;
        _userRoleRepository = userRoleRepository;
        _globalSettingsRepository = globalSettingsRepository;

        // setup is not done yet
        if (!_globalSettingsRepository.Initialized().GetAwaiter().GetResult())
        {
            _isSuperAdmin = true;
        }
        else
        {
            // check for current user if is super admin
            var globalSettings = _globalSettingsRepository.Get().GetAwaiter().GetResult();
            if (globalSettings != null && globalSettings.SuperAdmins.Contains(_apiExecutionContext.Username))
                _isSuperAdmin = true;
        }

        _userRoles = userRoleRepository.GetByUserId(_apiExecutionContext.UserId).GetAwaiter().GetResult().ToList();
    }

    public async Task<IEnumerable<T>> HasAccess<T>(IEnumerable<T> entities, string action, CancellationToken cancellationToken = default) where T : IEntity
    {
        if (_isSuperAdmin)
            return entities;

        return await Task.FromResult(entities);
    }

    public Task<bool> HasAccess(GlobalPermissionAction action, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_isSuperAdmin);
    }

    public async Task<bool> HasAccess(Guid siteId, SitePermissionAction action, CancellationToken cancellationToken = default)
    {
        // check for super admin
        if (_isSuperAdmin)
            return true;

        // check if the user has access to the site
        var permissions = await _permissionRepository.GetByEntityId(siteId, cancellationToken);

        switch (action)
        {
            case SitePermissionAction.SiteContributor:
                return permissions.Any(p => p.EntityId == siteId && (p.Action == Enum.GetName(action) || p.Action == Enum.GetName(SitePermissionAction.SiteAdmin)));

            case SitePermissionAction.SiteAdmin:
                return permissions.Any(p => p.EntityId == siteId && (p.Action == Enum.GetName(action)));

            default:
                break;
        }

        return false;
    }

    //public async Task<bool> HasAccess(Guid siteId, Guid PageId, PagePermissionAction action, CancellationToken cancellationToken = default)
    //{
    //    // check if the user access to the site 
    //    var hasSiteAccess = await HasAccess(siteId, SitePermissionAction.SiteContributor, cancellationToken);
    //    if (hasSiteAccess)
    //        return true;

    //    // check if the user has access to the page
    //    var permissions = await _permissionRepository.GetByEntityId(PageId, cancellationToken);

    //    switch (action)
    //    {
    //        case PagePermissionAction.PageView:
    //            break;
    //        case PagePermissionAction.PageContributor:
    //            break;
    //        case PagePermissionAction.PageAdmin:
    //            break;
    //        default:
    //            break;
    //    }

    //    return false;
    //}

    //public Task<bool> HasAccess(Guid siteId, Guid PageId, Guid pluginId, PluginPermissionAction action, CancellationToken cancellationToken = default)
    //{
    //    throw new NotImplementedException();
    //}
}
