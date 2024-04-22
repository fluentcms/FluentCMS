using FluentCMS.Web.UI.Services.Cookies;

namespace Microsoft.Extensions.DependencyInjection;

public static class CookiesServiceCollectionExtension
{
    public static IServiceCollection AddCookiesOnlyForClient(this IServiceCollection services)
    {
        services.AddScoped<ICookieService, BrowserCookieService>();
        return services;
    }

    public static IServiceCollection AddCookies(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();

        services.AddScoped<BrowserCookieService>();
        services.AddScoped<HttpContextCookieService>();

        services.AddScoped<ICookieService>(x =>
        {
            var httpContextAccessor = x.GetRequiredService<IHttpContextAccessor>();
            var httpContext = httpContextAccessor.HttpContext;
            var isPrerendering = (httpContext is not null && !httpContext.Response.HasStarted);

            return isPrerendering
                ? x.GetRequiredService<HttpContextCookieService>()
                : x.GetRequiredService<BrowserCookieService>();
        });

        return services;
    }
}
