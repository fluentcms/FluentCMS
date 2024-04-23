using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Configuration;

namespace Microsoft.Extensions.DependencyInjection;

public static class AdminUIServiceExtensions
{
    public static IServiceCollection AddAdminUIServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IAuthService, AuthService>();
        services.AddAuthorization();
        services.AddAuthentication(options =>
        {

            options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            options.DefaultAuthenticateScheme = CookieAuthenticationDefaults.AuthenticationScheme;

        }).AddCookie(options =>
        {
            options.LoginPath = "/auth/login";
            options.LogoutPath = "/auth/logout";
            options.AccessDeniedPath = "/auth/access-denied";
        });

        services.AddLocalStorage();
        services.AddCookies();
        services.AddApiClients(configuration);
        services.AddScoped<SetupManager>();
        services.AddErrorMessageFactory();
        //services.AddScoped<AuthStateProvider>();
        //services.AddScoped<AuthenticationStateProvider, AuthStateProvider>(c => c.GetRequiredService<AuthStateProvider>());
        //services.AddScoped<AuthenticationStateProvider>();
        //services.AddCascadingAuthenticationState();

        return services;
    }
}
