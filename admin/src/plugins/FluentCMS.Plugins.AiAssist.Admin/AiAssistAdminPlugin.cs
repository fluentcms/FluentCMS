using FluentCMS.Admin.Plugins;
using FluentCMS.Plugins.AiAssist.Admin;

[assembly: FluentCmsAdminPlugin(typeof(AiAssistAdminPlugin))]

namespace FluentCMS.Plugins.AiAssist.Admin;

[FluentCmsAdminPlugin("ai", Version = "1.0.0", AdminRange = "[1.0.0,2.0.0)")]
public sealed class AiAssistAdminPlugin : IAdminPlugin
{
    public void Configure(IAdminPluginBuilder admin) => ArgumentNullException.ThrowIfNull(admin);
}
