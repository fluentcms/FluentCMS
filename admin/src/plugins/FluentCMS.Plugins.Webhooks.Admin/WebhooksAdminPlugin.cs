using FluentCMS.Admin.Plugins;
using FluentCMS.Plugins.Webhooks.Admin;

[assembly: FluentCmsAdminPlugin(typeof(WebhooksAdminPlugin))]

namespace FluentCMS.Plugins.Webhooks.Admin;

[FluentCmsAdminPlugin("webhooks", Version = "1.0.0", AdminRange = "[1.0.0,2.0.0)")]
public sealed class WebhooksAdminPlugin : IAdminPlugin
{
    public void Configure(IAdminPluginBuilder admin) => ArgumentNullException.ThrowIfNull(admin);
}
