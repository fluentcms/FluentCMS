namespace FluentCMS.Services.Permissions;

public class PermissionManager : IPermissionManager
{
    private readonly IAuthContext _authContext;
    private readonly IPermissionRepository _permissionRepository;
    private readonly IUserRoleRepository _userRoleRepository;
    private readonly IGlobalSettingsRepository _globalSettingsRepository;
    private IEnumerable<Guid> _userRoleIds = [];
    private IEnumerable<Permission> _sitePermissions = [];

    public PermissionManager(IAuthContext authContext, IPermissionRepository permissionRepository, IUserRoleRepository userRoleRepository, IGlobalSettingsRepository globalSettingsRepository)
    {
        _authContext = authContext;
        _permissionRepository = permissionRepository;
        _userRoleRepository = userRoleRepository;
        _globalSettingsRepository = globalSettingsRepository;
    }

    public async Task<bool> HasAccess<TEntity>(TEntity entity, string action, CancellationToken cancellationToken = default) where TEntity : ISiteAssociatedEntity
    {
        if (await IsSuperAdmin(cancellationToken))
            return true;

        //if (!await _setupManager.IsInitialized())
        //    return true;

        return true;

        //TODO 
        _userRoleIds = (await _userRoleRepository.GetUserRoles(_authContext.UserId, entity.SiteId, cancellationToken)).Select(x => x.RoleId);
        _sitePermissions = await _permissionRepository.GetAllForSite(entity.SiteId, cancellationToken);

        switch (entity.GetType().Name)
        {
            case nameof(Site): return CheckSiteAccess(entity.Id, action);
            case nameof(Page): return CheckPageAccess(entity.Id, entity.SiteId, action);
            case nameof(Plugin): return CheckPluginAccess(entity.Id, (entity as Plugin).PageId, entity.SiteId, action); ;

            default: break;
        }

        return false;
    }

    public async Task<IEnumerable<TEntity>> HasAccess<TEntity>(IEnumerable<TEntity> entities, string action, CancellationToken cancellationToken = default) where TEntity : ISiteAssociatedEntity
    {
        var accessibleEntities = new List<TEntity>();

        foreach (var entity in entities)
            if (await HasAccess(entity, action, cancellationToken))
                accessibleEntities.Add(entity);

        return accessibleEntities;

    }

    private async Task<bool> IsSuperAdmin(CancellationToken cancellationToken)
    {
        var globalSettings = await _globalSettingsRepository.Get(cancellationToken);
        if (globalSettings == null)
            return false;

        return globalSettings.SuperAdmins.Contains(_authContext.Username);
    }

    private bool CheckSiteAccess(Guid siteId, string action)
    {
        var sitePermissions = _sitePermissions.Where(x => x.EntityId == siteId);
        var validActions = GetValidActions(nameof(Site), action);

        foreach (var validAction in validActions)
        {
            var hasAccess = sitePermissions.Any(x => x.Action == validAction && _userRoleIds.Contains(x.RoleId));
            if (hasAccess)
                return true;
        }

        return false;
    }

    private bool CheckPageAccess(Guid pageId, Guid siteId, string action)
    {
        var hasSiteAccess = CheckSiteAccess(siteId, PermissionActionNames.SiteContributor);
        if (hasSiteAccess)
            return true;

        var pagePermissions = _sitePermissions.Where(x => x.EntityId == pageId);
        var validActions = GetValidActions(nameof(Page), action);

        foreach (var validAction in validActions)
        {
            var hasAccess = pagePermissions.Any(x => x.Action == validAction && _userRoleIds.Contains(x.RoleId));
            if (hasAccess)
                return true;
        }

        return false;
    }

    private bool CheckPluginAccess(Guid pluginId, Guid pageId, Guid siteId, string action)
    {
        var hasPageAccess = CheckPageAccess(pageId, siteId, PermissionActionNames.PageView);
        if (hasPageAccess)
            return true;

        var pluginPermissions = _sitePermissions.Where(x => x.EntityId == pluginId);
        var validActions = GetValidActions(nameof(Plugin), action);

        foreach (var validAction in validActions)
        {
            var hasAccess = pluginPermissions.Any(x => x.Action == validAction && _userRoleIds.Contains(x.RoleId));
            if (hasAccess)
                return true;
        }

        return false;
    }

    private IEnumerable<string> GetValidActions(string entityType, string action)
    {
        return entityType switch
        {
            nameof(Page) => GetPageValidActions(action),
            nameof(Site) => GetSiteValidActions(action),
            nameof(Plugin) => GetPluginValidActions(action),
            _ => [],
        };
    }

    private static IEnumerable<string> GetSiteValidActions(string action)
    {
        if (action == PermissionActionNames.SiteContributor)
        {
            yield return PermissionActionNames.SiteAdmin;
            yield return PermissionActionNames.SiteContributor;
        }
        else if (action == PermissionActionNames.SiteAdmin)
        {
            yield return PermissionActionNames.SiteAdmin;
        }
    }

    private static IEnumerable<string> GetPageValidActions(string action)
    {
        if (action == PermissionActionNames.PageView)
        {
            yield return PermissionActionNames.PageAdmin;
            yield return PermissionActionNames.PageContributor;
            yield return PermissionActionNames.PageView;
        }
        else if (action == PermissionActionNames.PageContributor)
        {
            yield return PermissionActionNames.PageAdmin;
            yield return PermissionActionNames.PageContributor;
        }
        else if (action == PermissionActionNames.PageAdmin)
        {
            yield return PermissionActionNames.PageAdmin;
        }
    }

    private static IEnumerable<string> GetPluginValidActions(string action)
    {
        if (action == PermissionActionNames.PluginView)
        {
            yield return PermissionActionNames.PluginAdmin;
            yield return PermissionActionNames.PluginContributor;
            yield return PermissionActionNames.PluginView;
        }
        else if (action == PermissionActionNames.PluginContributor)
        {
            yield return PermissionActionNames.PluginAdmin;
            yield return PermissionActionNames.PluginContributor;
        }
        else if (action == PermissionActionNames.PluginAdmin)
        {
            yield return PermissionActionNames.PluginAdmin;
        }
    }
}
