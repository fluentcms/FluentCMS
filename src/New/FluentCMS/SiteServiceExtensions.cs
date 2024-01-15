using FluentCMS.Web.ApiClients;
using FluentCMS.Web.UI;
using FluentCMS.Web.UI.Services;

namespace Microsoft.Extensions.DependencyInjection;

public static class SiteServiceExtensions
{
    public static IServiceCollection AddSiteServices(this IServiceCollection services)
    {
        services.AddAdminUIServices();

        services.AddScoped<ErrorManager>();

        services.AddScoped<IErrorHandler, FallbackErrorHandler>();
        services.AddScoped<IErrorHandler<ApiClientException>, ApiClientExceptionErrorHandler>();

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
