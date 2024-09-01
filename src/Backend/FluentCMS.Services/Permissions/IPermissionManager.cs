namespace FluentCMS.Services.Permissions;

public interface IPermissionManager
{
    Task<bool> HasSiteAccess(Site site, string action, CancellationToken cancellationToken = default);

    Task<bool> HasPageAccess(Page page, string action, CancellationToken cancellationToken = default);

    Task<bool> HasPluginAccess(Plugin plugin, string action, CancellationToken cancellationToken = default);

    Task<IEnumerable<Site>> HasSiteAccess(IEnumerable<Site> sites, string action, CancellationToken cancellationToken = default);

    Task<IEnumerable<Page>> HasPageAccess(IEnumerable<Page> pages, string action, CancellationToken cancellationToken = default);

    Task<IEnumerable<Plugin>> HasPluginAccess(IEnumerable<Plugin> plugins, string action, CancellationToken cancellationToken = default);
}
