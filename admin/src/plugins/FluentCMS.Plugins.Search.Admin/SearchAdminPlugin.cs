using FluentCMS.Admin.Plugins;
using FluentCMS.Plugins.Search.Admin;

[assembly: FluentCmsAdminPlugin(typeof(SearchAdminPlugin))]

namespace FluentCMS.Plugins.Search.Admin;

[FluentCmsAdminPlugin("search", Version = "1.0.0", AdminRange = "[1.0.0,2.0.0)")]
public sealed class SearchAdminPlugin : IAdminPlugin
{
    public void Configure(IAdminPluginBuilder admin) => ArgumentNullException.ThrowIfNull(admin);
}
