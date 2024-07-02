﻿using FluentCMS.Web.UI;
using FluentCMS.Web.UI.DynamicRendering;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Configuration;
using System.Security.Claims;
using System.Web;

namespace Microsoft.Extensions.DependencyInjection;

public static class ServiceExtensions
{
    public static IServiceCollection AddCmsServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<PluginLoader>();

        services.AddScoped<ILayoutProcessor, LayoutProcessor>();

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
            var page = pageResponse?.Data;

            var viewContext = new ViewContext
            {
                Layout = page!.Layout,
                Page = page,
                Site = page.Site,
                UserLogin = uerLogin,
                Type = ViewType.Default
            };

            // check if the page is in edit mode
            // it should have pluginId and pluginViewName query strings
            var uriBuilder = new UriBuilder(navigationManager.Uri);
            var queryParams = HttpUtility.ParseQueryString(uriBuilder.Query);
            if (queryParams["pluginId"] != null && queryParams["viewName"] != null)
            {
                // check if the pluginId is valid
                if (Guid.TryParse(queryParams["pluginId"], out var pluginId))
                {
                    viewContext.Type = ViewType.PluginEdit;
                    viewContext.PluginId = pluginId;
                    viewContext.PluginViewName = queryParams["viewName"];
                    viewContext.Plugin = page.Sections!.Values.SelectMany(x => x).Single(p => p.Id == pluginId);
                }
            }

            if (queryParams["pageEdit"] != null)
            {
                viewContext.Type = ViewType.PageEdit;
            }

            if (queryParams["pagePreview"] != null)
            {
                viewContext.Type = ViewType.PagePreview;
            }

            return viewContext;
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
