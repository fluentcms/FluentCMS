using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Configuration;

namespace Microsoft.Extensions.DependencyInjection;

public static class AdminUIServiceExtensions
{
    public static IServiceCollection AddAdminUIServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddAutoMapper(typeof(AdminUIServiceExtensions));
        services.AddScoped<IAuthService, AuthService>();
        services.AddAuthorization();
        services.AddAuthentication(options =>
        {
            options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            options.DefaultAuthenticateScheme = CookieAuthenticationDefaults.AuthenticationScheme;

        }).AddCookie();

        services.AddLocalStorage();
        services.AddCookies();
        services.AddApiClients(configuration);
        services.AddScoped<SetupManager>();

        return services;
    }
}
