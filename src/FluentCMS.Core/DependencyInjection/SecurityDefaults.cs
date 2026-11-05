using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.DependencyInjection;

// Defaults of 4.7 that need no endpoint. A host's own Configure, registered after AddFluentCms, still wins.
internal static class SecurityDefaults
{
    public static void AddSecurityDefaults(this IServiceCollection services)
    {
        services.ConfigureAll<CookieAuthenticationOptions>(options =>
        {
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
        });

        services.Configure<AntiforgeryOptions>(options =>
        {
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.HttpOnly = true;
        });
    }
}
