using FluentCMS.Admin.Plugins;
using FluentCMS.Plugins.MultiSite.Admin;

[assembly: FluentCmsAdminPlugin(typeof(MultiSiteAdminPlugin))]

namespace FluentCMS.Plugins.MultiSite.Admin;

[FluentCmsAdminPlugin("multi-site", Version = "1.0.0", AdminRange = "[1.0.0,2.0.0)")]
public sealed class MultiSiteAdminPlugin : IAdminPlugin
{
    public void Configure(IAdminPluginBuilder admin) => ArgumentNullException.ThrowIfNull(admin);
}
