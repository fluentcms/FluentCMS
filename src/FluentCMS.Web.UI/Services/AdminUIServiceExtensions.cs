using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Configuration;

namespace Microsoft.Extensions.DependencyInjection;

public static class AdminUIServiceExtensions
{
    public static IServiceCollection AddAdminUIServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddAutoMapper(typeof(AdminUIServiceExtensions));

        services.AddAuthorization();
        services.AddAuthentication(options =>
        {
            options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            options.DefaultAuthenticateScheme = CookieAuthenticationDefaults.AuthenticationScheme;

        }).AddCookie();

        services.AddApiClients(configuration);
        services.AddScoped<SetupManager>();
        services.AddScoped<AuthManager>();
        services.AddCascadingAuthenticationState();

        return services;
    }
}
