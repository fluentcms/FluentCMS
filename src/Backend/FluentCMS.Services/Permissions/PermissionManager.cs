namespace FluentCMS.Services.Permissions;

public class PermissionManager : IPermissionManager
{
    private readonly IApiExecutionContext _apiExecutionContext;
    private readonly IPermissionRepository _permissionRepository;
    private readonly IUserRoleRepository _userRoleRepository;
    private readonly IGlobalSettingsRepository _globalSettingsRepository;
    private IEnumerable<Guid> _userRoleIds = [];
    private IEnumerable<Permission> _sitePermissions = [];

    public PermissionManager(IApiExecutionContext apiExecutionContext, IPermissionRepository permissionRepository, IUserRoleRepository userRoleRepository, IGlobalSettingsRepository globalSettingsRepository)
    {
        _apiExecutionContext = apiExecutionContext;
        _permissionRepository = permissionRepository;
        _userRoleRepository = userRoleRepository;
        _globalSettingsRepository = globalSettingsRepository;
    }

    #region Single Object Check

    public async Task<bool> HasSiteAccess(Site site, string action, CancellationToken cancellationToken = default)
    {
        return true;
        if (await HasSpecialPermission(cancellationToken))
            return true;

        return await CheckSiteAccess(site.Id, action, cancellationToken);
    }

    public async Task<bool> HasPageAccess(Page page, string action, CancellationToken cancellationToken = default)
    {
        return true;
        if (await HasSpecialPermission(cancellationToken))
            return true;

        return await CheckPageAccess(page.Id, page.SiteId, action, cancellationToken);
    }

    public async Task<bool> HasPluginAccess(Plugin plugin, string action, CancellationToken cancellationToken = default)
    {
        return true;
        if (await HasSpecialPermission(cancellationToken))
            return true;

        return await CheckPluginAccess(plugin.Id, plugin.PageId, plugin.SiteId, action, cancellationToken);
    }

    #endregion

    #region List Objects Check

    public async Task<IEnumerable<Site>> HasSiteAccess(IEnumerable<Site> sites, string action, CancellationToken cancellationToken = default)
    {
        var accessibleSites = new List<Site>();

        foreach (var site in sites)
            if (await HasSiteAccess(site, action, cancellationToken))
                accessibleSites.Add(site);

        return accessibleSites;
    }

    public async Task<IEnumerable<Page>> HasPageAccess(IEnumerable<Page> pages, string action, CancellationToken cancellationToken = default)
    {
        var accessiblePages = new List<Page>();

        foreach (var page in pages)
            if (await HasPageAccess(page, action, cancellationToken))
                accessiblePages.Add(page);

        return accessiblePages;
    }

    public async Task<IEnumerable<Plugin>> HasPluginAccess(IEnumerable<Plugin> plugins, string action, CancellationToken cancellationToken = default)
    {
        var accessiblePlugins = new List<Plugin>();

        foreach (var plugin in plugins)
            if (await HasPluginAccess(plugin, action, cancellationToken))
                accessiblePlugins.Add(plugin);

        return accessiblePlugins;
    }

    #endregion

    private async Task<bool> HasSpecialPermission(CancellationToken cancellationToken = default)
    {
        if (await IsSuperAdmin(cancellationToken))
            return true;

        //if (!await _setupManager.IsInitialized())
        //    return true;

        return true;
    }

    private async Task<bool> IsSuperAdmin(CancellationToken cancellationToken)
    {
        var globalSettings = await _globalSettingsRepository.Get(cancellationToken);
        if (globalSettings == null)
            return false;

        return globalSettings.SuperAdmins.Contains(_apiExecutionContext.Username);
    }

    private async Task<bool> CheckSiteAccess(Guid siteId, string action, CancellationToken cancellationToken)
    {
        var sitePermissions = await GetSitePermissions(siteId, cancellationToken);
        var validActions = GetValidActions(nameof(Site), action);
        var userRoleIds = await GetUserRoleIds(siteId, cancellationToken);

        foreach (var validAction in validActions)
        {
            var hasAccess = sitePermissions.Any(x => x.Action == validAction && userRoleIds.Contains(x.RoleId));
            if (hasAccess)
                return true;
        }

        return false;
    }

    private async Task<bool> CheckPageAccess(Guid pageId, Guid siteId, string action, CancellationToken cancellationToken)
    {
        var hasSiteAccess = await CheckSiteAccess(siteId, PermissionActionNames.SiteContributor, cancellationToken);

        if (hasSiteAccess)
            return true;

        var pagePermissions = (await GetSitePermissions(siteId, cancellationToken)).Where(x => x.EntityId == pageId);
        var validActions = GetValidActions(nameof(Page), action);

        foreach (var validAction in validActions)
        {
            var hasAccess = pagePermissions.Any(x => x.Action == validAction && _userRoleIds.Contains(x.RoleId));
            if (hasAccess)
                return true;
        }

        return false;
    }

    private async Task<bool> CheckPluginAccess(Guid pluginId, Guid pageId, Guid siteId, string action, CancellationToken cancellationToken)
    {
        var hasPageAccess = await CheckPageAccess(pageId, siteId, PermissionActionNames.PageView, cancellationToken);
        if (hasPageAccess)
            return true;

        var pluginPermissions = (await GetSitePermissions(siteId, cancellationToken)).Where(x => x.EntityId == pluginId);
        var validActions = GetValidActions(nameof(Plugin), action);
        var userRoleIds = await GetUserRoleIds(siteId, cancellationToken);

        foreach (var validAction in validActions)
        {
            var hasAccess = pluginPermissions.Any(x => x.Action == validAction && userRoleIds.Contains(x.RoleId));
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

    private async Task<IEnumerable<Permission>> GetSitePermissions(Guid siteId, CancellationToken cancellationToken)
    {
        _sitePermissions ??= await _permissionRepository.GetAllForSite(siteId, cancellationToken);

        return _sitePermissions;
    }

    private async Task<IEnumerable<Guid>> GetUserRoleIds(Guid siteId, CancellationToken cancellationToken)
    {
        _userRoleIds ??= (await _userRoleRepository.GetUserRoles(_apiExecutionContext.UserId, siteId, cancellationToken)).Select(x => x.RoleId);

        return _userRoleIds;
    }
}

