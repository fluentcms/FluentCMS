using FluentCMS.Content;
using FluentCMS.Hooks;
using FluentCMS.Plugins;
using FluentCms_Plugin1;
using Microsoft.Extensions.DependencyInjection;

// A host that references this assembly discovers the plugin; cms.AddPlugin<StarterPlugin>() adds it explicitly.
[assembly: FluentCmsPlugin(typeof(StarterPlugin))]

namespace FluentCms_Plugin1;

// The manifest: the name is lower kebab-case, at most 32 characters, and names the tables (cms_starter_plugin_*), the
// settings area and the routes. The version is the assembly's, and the SDK range the versions of FluentCMS.Sdk it runs on.
[FluentCmsPlugin(Name, SdkRange = "[1.0.0,2.0.0)")]
public sealed class StarterPlugin : IPlugin
{
    public const string Name = "starter-plugin";

    public void ConfigureServices(IPluginServiceBuilder plugin)
    {
        plugin.Services.AddScoped<IAfterPublishHook<ContentDocument>, StarterHook>();
        plugin
            .AddPermissions<StarterPermissions>()
            .AddSettings<StarterSettings>()
            .AddModel<StarterModel>()
            .AddMigrations(typeof(StarterPlugin).Assembly);
    }

    // Under /api/management/v1/starter-plugin, with the management scope and the Management API's limits and CORS.
    public void Configure(IPluginAppBuilder app) =>
        app.MapManagement("/" + Name).MapStarterEndpoints();
}
