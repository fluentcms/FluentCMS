using FluentCMS.Events;
using FluentCMS.Plugins;
using FluentCMS.Plugins.MultiSite;
using FluentCMS.Sites;
using Microsoft.Extensions.DependencyInjection;

[assembly: FluentCmsPlugin(typeof(MultiSitePlugin))]

namespace FluentCMS.Plugins.MultiSite;

[FluentCmsPlugin("multi-site", SdkRange = "[1.0.0,2.0.0)")]
public sealed class MultiSitePlugin : IPlugin
{
    public void ConfigureServices(IPluginServiceBuilder plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);

        plugin.Services.AddSingleton<SiteHostMap>();
        plugin.Services.AddSingleton<ISiteHostResolver>(services => services.GetRequiredService<SiteHostMap>());
        plugin.Services.AddScoped<SiteHostService>();

        plugin
            .AddModel<MultiSiteModel>()
            .AddMigrations(typeof(MultiSitePlugin).Assembly)
            .AddEventHandler<SiteDeletedEvent, DeletedSiteHosts>()
            .AddErrorCode(MultiSiteErrorCodes.InvalidHostName, 422, "Invalid host name")
            .AddErrorCode(MultiSiteErrorCodes.WildcardHostName, 422, "Wildcard host name")
            .AddErrorCode(MultiSiteErrorCodes.HostNameWithPort, 422, "Host name with a port")
            .AddErrorCode(MultiSiteErrorCodes.HostNameTaken, 409, "Host name taken")
            .AddErrorCode(MultiSiteErrorCodes.HostNotFound, 404, "Host name not found")
            .AddErrorCode(MultiSiteErrorCodes.PrimaryHostNeeded, 409, "Primary host needed");
    }

    public void Configure(IPluginAppBuilder app) => app.MapManagement("/sites").MapMultiSiteEndpoints();
}
