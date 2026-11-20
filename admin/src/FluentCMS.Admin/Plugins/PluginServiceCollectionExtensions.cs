using FluentCMS.Admin.Shell;
using Microsoft.AspNetCore.Components.WebAssembly.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FluentCMS.Admin.Plugins;

internal static class PluginServiceCollectionExtensions
{
    // The halves of the catalog registered before, or none. Their menu items join the sidebar's menu, with core's.
    public static IServiceCollection AddAdminPlugins(this IServiceCollection services)
    {
        services.TryAddSingleton(AdminPluginCatalog.Empty);
        services.TryAddSingleton(provider => new AdminMenu { Entries = [.. AdminMenu.Core, .. MenuEntries(provider.GetRequiredService<AdminPluginCatalog>())] });
        services.TryAddSingleton<LazyAssemblyLoader>();
        services.TryAddScoped<IAdminAssemblyLoader, LazyAdminAssemblyLoader>();
        services.TryAddScoped<AdministrationAssembly>();
        services.AddSingleton(new AdminServiceDescriptors(services));
        services.AddScoped<AdminPlugins>();
        services.AddScoped<IAdminPluginServices>(provider => provider.GetRequiredService<AdminPlugins>());
        return services;
    }

    private static IEnumerable<AdminMenuEntry> MenuEntries(AdminPluginCatalog catalog) =>
        catalog.Halves.SelectMany(half => half.MenuItems.Select(item => new AdminMenuEntry(
            item.Route.TrimStart('/'),
            item.Label,
            item.Section switch
            {
                AdminSection.Media => AdminMenuSection.Media,
                AdminSection.Administration => AdminMenuSection.Administration,
                AdminSection.Settings => AdminMenuSection.Settings,
                _ => AdminMenuSection.Content,
            },
            item.Permission,
            half.Name,
            item)));
}
