using FluentCMS.Web.ApiClients;
using FluentCMS.Web.UI;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Configuration;
using System.Security.Claims;

namespace Microsoft.Extensions.DependencyInjection;

public static class CmsServiceExtensions
{
    public static IServiceCollection AddCmsServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<PluginLoader>();

        // Add services to the container.
        services.AddRazorComponents()
            .AddInteractiveServerComponents();

        services.AddAuthorization();
        services.AddAuthentication(options =>
        {
            options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            options.DefaultAuthenticateScheme = CookieAuthenticationDefaults.AuthenticationScheme;

        }).AddCookie();

        services.AddApiClients(configuration);

        // add UserLoginResponse as scoped to be supported in SSR and InteractiveMode
        services.AddScoped(sp =>
        {
            var authStateProvider = sp.GetRequiredService<AuthenticationStateProvider>();
            var authStateTask = authStateProvider.GetAuthenticationStateAsync();
            var authState = authStateTask.GetAwaiter().GetResult();

            if (authState?.User?.Identity == null || !authState.User.Identity.IsAuthenticated)
                return new UserLoginResponse();

            return new UserLoginResponse
            {
                UserId = Guid.Parse(authState.User.FindFirstValue(ClaimTypes.Sid) ?? Guid.Empty.ToString()),
                Email = authState.User.FindFirstValue(ClaimTypes.Email),
                UserName = authState.User.FindFirstValue(ClaimTypes.NameIdentifier),
                Token = authState.User.FindFirstValue("jwt"),
                RoleIds = authState.User.FindAll(ClaimTypes.Role).Select(x => Guid.Parse(x.Value)).ToList()
            };
        });


        // add global cascade parameter for Page (PageFullDetailResponse)
        // if we use cascading value in the component, it will be null in component which are rendered as InteractiveMode
        // because the page is SSR and the cascading value is not available in the component
        // so we set the cascading value globally in the service
        // https://github.com/dotnet/aspnetcore/issues/53482
        services.AddCascadingValue(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var navigationManager = sp.GetRequiredService<NavigationManager>();
            var uerLogin = sp.GetRequiredService<UserLoginResponse>();

            var pageClient = httpClientFactory.CreateApiClient<PageClient>(uerLogin);
            var pageResponse = pageClient.GetByUrlAsync(navigationManager.Uri).GetAwaiter().GetResult();

            return pageResponse.Data ?? null;
        });

        return services;
    }

    public static IApplicationBuilder UseCmsServices(this WebApplication app)
    {
        app.UseAntiforgery();

        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();

        return app;
    }
}
