using FluentCMS.Web.UI;

namespace Microsoft.Extensions.DependencyInjection;

public static class SiteServiceExtensions
{
    public static IServiceCollection AddSiteServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddAdminUIServices(configuration);

        // Add services to the container.
        services.AddRazorComponents()
            .AddInteractiveServerComponents();

        return services;
    }

    public static IApplicationBuilder UseSiteServices(this WebApplication app)
    {
        app.UseAntiforgery();

        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();

        return app;
    }

}
