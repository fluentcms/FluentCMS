﻿using AutoMapper;
using FluentCMS;
using FluentCMS.Web.UI;
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
        services.AddAutoMapper(typeof(FluentCMS.Web.UI.MappingProfile));

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
            };
        });

        // add global cascade parameter for Page (PageFullDetailResponse)
        // if we use cascading value in the component, it will be null in component which are rendered as InteractiveMode
        // because the page is SSR and the cascading value is not available in the component
        // so we set the cascading value globally in the service
        // https://github.com/dotnet/aspnetcore/issues/53482
        services.AddCascadingValue(sp =>
        {
            var viewState = new ViewState();
            var navigationManager = sp.GetRequiredService<NavigationManager>();
            var apiClient = sp.GetRequiredService<ApiClientFactory>();
            var mapper = sp.GetRequiredService<IMapper>();

            var pageResponse = apiClient.Page.GetByUrlAsync(navigationManager.Uri).GetAwaiter().GetResult();

            if (pageResponse?.Data == null)
                throw new Exception("Error while loading ViewState");

            viewState.Page = mapper.Map<PageViewState>(pageResponse.Data);
            viewState.Layout = mapper.Map<LayoutViewState>(pageResponse.Data.Layout);
            viewState.DetailLayout = mapper.Map<LayoutViewState>(pageResponse.Data.DetailLayout);
            viewState.EditLayout = mapper.Map<LayoutViewState>(pageResponse.Data.EditLayout);
            viewState.Site = mapper.Map<SiteViewState>(pageResponse.Data.Site);
            viewState.Plugins = pageResponse.Data.Sections!.Values.SelectMany(x => x).Select(p => mapper.Map<PluginViewState>(p)).ToList();
            viewState.User = mapper.Map<UserViewState>(pageResponse.Data.User);

            // check if the page is in edit mode
            // it should have pluginId and pluginViewName query strings
            var uriBuilder = new UriBuilder(navigationManager.Uri);
            var queryParams = HttpUtility.ParseQueryString(uriBuilder.Query);
            if (queryParams["pluginId"] != null && queryParams["viewName"] != null)
            {
                // check if the pluginId is valid
                if (Guid.TryParse(queryParams["pluginId"], out var pluginId))
                {
                    // TODO: Decide when show edit and when show detail view
                    if (queryParams["viewMode"] == "detail")
                    {
                        viewState.Type = ViewStateType.PluginDetail;
                    }
                    else
                    {
                        viewState.Type = ViewStateType.PluginEdit;
                    }
                    viewState.Plugin = viewState.Plugins.Single(x => x.Id == pluginId);
                    viewState.PluginViewName = queryParams["viewName"];
                }
            }

            if (queryParams["pageEdit"] != null)
                viewState.Type = ViewStateType.PageEdit;

            if (queryParams["pagePreview"] != null)
                viewState.Type = ViewStateType.PagePreview;

            return viewState;
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

