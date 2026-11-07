namespace FluentCMS.Core.Storage.Entities;

// A row of cms_plugin_site: whether one plugin is enabled for one site. A plugin with no row for a site is enabled there.
// Write it through PluginAdministration, which checks plugins.manage and tells the cache and the event bus.
public sealed class PluginSiteRow
{
    public const int PluginMaxLength = 32;

    public string Plugin { get; set; } = "";

    public Guid SiteId { get; set; }

    public bool Enabled { get; set; }
}
