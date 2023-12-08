using FluentCMS.Web.UI.Services;

namespace Microsoft.Extensions.DependencyInjection;

public static class UIServiceExtensions
{

    public static IServiceCollection AddUIServices(this IServiceCollection services)
    {
        services.AddScoped<AppStateService>();
        services.AddApiClients();

        // Add services to the container.
        services.AddRazorComponents()
            .AddInteractiveServerComponents();

        return services;
    }
}
