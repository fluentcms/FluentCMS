using FluentCMS.Admin.Plugins;
using FluentCMS.Plugins.Mcp.Admin;

[assembly: FluentCmsAdminPlugin(typeof(McpAdminPlugin))]

namespace FluentCMS.Plugins.Mcp.Admin;

[FluentCmsAdminPlugin("mcp", Version = "1.0.0", AdminRange = "[1.0.0,2.0.0)")]
public sealed class McpAdminPlugin : IAdminPlugin
{
    public void Configure(IAdminPluginBuilder admin) => ArgumentNullException.ThrowIfNull(admin);
}
