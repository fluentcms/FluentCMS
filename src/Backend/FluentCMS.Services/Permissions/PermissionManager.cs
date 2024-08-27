namespace FluentCMS.Services.Permissions;

public class PermissionManager<TEntity> : IPermissionManager<TEntity> where TEntity : ISiteAssociatedEntity
{
    private IAuthContext _authContext;
    private readonly IPermissionRepository _permissionRepository;
    private readonly IPageRepository _pageRepository;

    public PermissionManager(IAuthContext authContext, IPermissionRepository permissionRepository, IPageRepository pageRepository)
    {
        _authContext = authContext;
        _permissionRepository = permissionRepository;
        _pageRepository = pageRepository;
    }

    public async Task<bool> HasAccess(TEntity entity, string action, CancellationToken cancellationToken = default)
    {
        //TODO 
        IEnumerable<Guid> userRoleIds = new List<Guid>();

        switch (entity.GetType().Name)
        {
            case nameof(Page): return await CheckPageAccess(entity as Page, action, userRoleIds, cancellationToken);
            case nameof(Site): return await CheckSiteAccess(entity.Id, action, userRoleIds, cancellationToken);
            case nameof(Plugin): return await CheckPluginAccess(entity as Plugin, action, userRoleIds, cancellationToken);

            default: break;
        }

        return false;
    }

    private async Task<bool> CheckSiteAccess(Guid siteId, string action, IEnumerable<Guid> userRoleIds, CancellationToken cancellationToken = default)
    {
        var sitePermissions = await _permissionRepository.GetByEntityId(siteId, cancellationToken);
        var validActions = GetValidActions(nameof(Site), action);

        foreach (var validAction in validActions)
        {
            var hasAccess = sitePermissions.Any(x => x.Action == validAction && userRoleIds.Contains(x.RoleId));
            if (hasAccess)
                return true;
        }

        return false;
    }

    private async Task<bool> CheckPageAccess(Page page, string action, IEnumerable<Guid> userRoleIds, CancellationToken cancellationToken = default)
    {
        var pagePermissions = await _permissionRepository.GetByEntityId(page.Id, cancellationToken);
        var validActions = GetValidActions(nameof(Page), action);

        foreach (var validAction in validActions)
        {
            var hasAccess = pagePermissions.Any(x => x.Action == validAction && userRoleIds.Contains(x.RoleId));
            if (hasAccess)
                return true;
        }

        return await CheckSiteAccess(page.SiteId, PermissionActionNames.SiteView, userRoleIds, cancellationToken);
    }

    private async Task<bool> CheckPluginAccess(Plugin plugin, string action, IEnumerable<Guid> userRoleIds, CancellationToken cancellationToken = default)
    {
        var pluginPermissions = await _permissionRepository.GetByEntityId(plugin.Id, cancellationToken);
        var validActions = GetValidActions(nameof(Plugin), action);

        foreach (var validAction in validActions)
        {
            var hasAccess = pluginPermissions.Any(x => x.Action == validAction && userRoleIds.Contains(x.RoleId));
            if (hasAccess)
                return true;
        }

        var page = await _pageRepository.GetById(plugin.PageId, cancellationToken);
        if (page == null)
            return false;

        return await CheckPageAccess(page, PermissionActionNames.SiteView, userRoleIds, cancellationToken);
    }

    private IEnumerable<string> GetValidActions(string entityType, string action)
    {
        switch (entityType)
        {
            case nameof(Page): return GetPageValidActions(action);
            case nameof(Site): return GetSiteValidActions(action);
            case nameof(Plugin): return GetPluginValidActions(action);

            default: return [];
        }
    }

    private IEnumerable<string> GetSiteValidActions(string action)
    {
        if (action == PermissionActionNames.SiteView)
        {
            yield return PermissionActionNames.SiteAdmin;
            yield return PermissionActionNames.SiteContributor;
            yield return PermissionActionNames.SiteAdmin;
        }
        else if (action == PermissionActionNames.SiteContributor)
        {
            yield return PermissionActionNames.SiteAdmin;
            yield return PermissionActionNames.SiteContributor;
        }
        else if (action == PermissionActionNames.SiteAdmin)
        {
            yield return PermissionActionNames.SiteAdmin;
        }
    }

    private IEnumerable<string> GetPageValidActions(string action)
    {
        if (action == PermissionActionNames.PageView)
        {
            yield return PermissionActionNames.PageAdmin;
            yield return PermissionActionNames.PageContributor;
            yield return PermissionActionNames.PageAdmin;
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

    private IEnumerable<string> GetPluginValidActions(string action)
    {
        if (action == PermissionActionNames.PluginView)
        {
            yield return PermissionActionNames.PluginAdmin;
            yield return PermissionActionNames.PluginContributor;
            yield return PermissionActionNames.PluginAdmin;
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
