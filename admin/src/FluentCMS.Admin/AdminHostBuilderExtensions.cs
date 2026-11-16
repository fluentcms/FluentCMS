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

        builder.RootComponents.Add<HeadOutlet>("head::after");

        using var http = new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) };
        var startup = await AdminConfigurationLoader.LoadAsync(http);
        if (!builder.Services.AddAdminStartup(startup))
        {
            return;
        }
    }

    // With an invalid configuration only the error page's state is registered, so nothing that calls the API can run.
    internal static bool AddAdminStartup(this IServiceCollection services, AdminStartup startup)
    {
        services.AddSingleton(startup);
        if (startup.Configuration is null)
        {
            return false;
        }

        services.AddSingleton(startup.Configuration);
        return true;
    }
}
