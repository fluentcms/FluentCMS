using FluentCMS.Web.UI;

namespace Microsoft.Extensions.DependencyInjection;

public static class SiteServiceExtensions
{
    public static IServiceCollection AddSiteServices(this IServiceCollection services)
    {
        services.AddAdminUIServices();

        // Add services to the container.
        services.AddRazorComponents()
            .AddInteractiveServerComponents();

        return services;
    }

    public static IApplicationBuilder UseSiteServices(this WebApplication app)
    {
        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();

        return app;
    }

}
