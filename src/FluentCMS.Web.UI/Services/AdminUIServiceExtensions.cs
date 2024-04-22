using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Configuration;

namespace Microsoft.Extensions.DependencyInjection;

public static class AdminUIServiceExtensions
{
    public static IServiceCollection AddAdminUIServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddLocalStorage();
        services.AddCookies();
        services.AddApiClients(configuration);
        services.AddScoped<SetupManager>();
        services.AddErrorMessageFactory();
        services.AddScoped<AuthStateProvider>();
        services.AddScoped<AuthenticationStateProvider, AuthStateProvider>(c => c.GetRequiredService<AuthStateProvider>());
        services.AddCascadingAuthenticationState();

        return services;
    }
}
