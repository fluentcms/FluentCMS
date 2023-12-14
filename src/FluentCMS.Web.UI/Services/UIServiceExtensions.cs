using FluentCMS.Web.UI;

namespace Microsoft.Extensions.DependencyInjection;

public static class UIServiceExtensions
{
    public static IServiceCollection AddUIServices(this IServiceCollection services)
    {
        services.AddScoped<AppStateService>();

        services.AddScoped(sp =>
        {
            var stateService = sp.GetRequiredService<AppStateService>();
            return stateService?.Current ??
                throw new InvalidOperationException("No current app state.");
        });

        services.AddApiClients();

        // Add services to the container.
        services.AddRazorComponents()
            .AddInteractiveServerComponents();

        return services;
    }

}
