using FluentCMS.Admin.Plugins;
using FluentCms_Plugin1.Admin;
using Microsoft.Extensions.DependencyInjection;

// The admin finds the half by this attribute when it loads the assembly.
[assembly: FluentCmsAdminPlugin(typeof(StarterAdminPlugin))]

namespace FluentCms_Plugin1.Admin;

// The name is the server plugin's, so the admin shows the half only in a site where that plugin is enabled. The admin range
// is the versions of FluentCMS.Admin.Abstractions the half runs on. Name, version and range are repeated in
// wwwroot/fluentcms-admin.json, which the admin reads before it loads the assembly; the manifest test keeps the two in step.
[FluentCmsAdminPlugin(Name, Version = "1.0.0", AdminRange = "[1.0.0,2.0.0)")]
public sealed class StarterAdminPlugin : IAdminPlugin
{
    public const string Name = "starter-plugin";

    // The server half's permission. The half never references the server half, so it names the permission itself.
    public const string NotesManage = "starter-plugin.notes.manage";

    // Runs once, when the assembly is first loaded. The services go into a provider of the half, which the components reach
    // through IAdminPluginServices and which falls back to the admin's services, IManagementApi among them.
    public void Configure(IAdminPluginBuilder admin) => admin.Services.AddScoped<StarterApi>();
}
