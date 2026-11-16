using FluentCMS.Admin.Configuration;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Admin;

public static class AdminHostBuilderExtensions
{
    // Asynchronous because it reads config.json before anything else registers (spec 2.5).
    public static async Task AddFluentCmsAdmin(this WebAssemblyHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // A team's app made from the standard template adds its own.
        if (!builder.RootComponents.Any(mapping => mapping.ComponentType == typeof(HeadOutlet)))
        {
            builder.RootComponents.Add<HeadOutlet>("head::after");
        }

        using var http = new HttpClient
        {
            BaseAddress = new Uri(builder.HostEnvironment.BaseAddress),
            Timeout = AdminConfigurationLoader.Timeout,
        };
        var startup = await AdminConfigurationLoader.LoadAsync(http);
        builder.Services.AddAdminStartup(startup);
    }

    // With an invalid configuration only the error page's state is registered.
    internal static IServiceCollection AddAdminStartup(this IServiceCollection services, AdminStartup startup)
    {
        services.AddSingleton(startup);
        if (startup.Configuration is { } configuration)
        {
            services.AddAdminServices(configuration);
        }

        return services;
    }

    // Every service of the admin registers here, and only from a valid configuration, so that nothing that calls the API can
    // run without one.
    internal static IServiceCollection AddAdminServices(this IServiceCollection services, AdminConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddSingleton(configuration);
        return services;
    }
}
