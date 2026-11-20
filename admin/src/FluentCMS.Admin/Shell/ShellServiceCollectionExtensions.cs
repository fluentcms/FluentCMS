using FluentCMS.Admin.Api;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FluentCMS.Admin.Shell;

internal static class ShellServiceCollectionExtensions
{
    // Before AddManagementApi: the shell's site and locale replace the signed-out defaults of the client.
    public static IServiceCollection AddAdminShell(this IServiceCollection services)
    {
        services.TryAddSingleton<AdminMenu>();
        services.AddScoped<ShellSite>();
        services.AddScoped<ICurrentSite>(provider => provider.GetRequiredService<ShellSite>());
        services.AddScoped<IContentLocale>(provider => provider.GetRequiredService<ShellSite>());
        services.AddScoped<AdminContext>();
        services.AddScoped<IAdminContext>(provider => provider.GetRequiredService<AdminContext>());
        services.AddScoped<IAdminAccess>(provider => provider.GetRequiredService<AdminContext>());
        services.TryAddScoped<PageCrumbs>();
        services.AddScoped<AdminOverlays>();
        services.AddScoped<IAdminOverlays>(provider => provider.GetRequiredService<AdminOverlays>());
        return services;
    }
}
