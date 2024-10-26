using AutoMapper;
using FluentCMS;
using FluentCMS.Web.UI;
using FluentCMS.Web.UI.DynamicRendering;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.Extensions.Configuration;
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

        services.AddViewState();

        return services;
    }

    public static IApplicationBuilder UseCmsServices(this WebApplication app)
    {
        app.UseAntiforgery();

        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();

        return app;
    }

    private static IServiceCollection AddViewState(this IServiceCollection services)
    {
        services.AddScoped(sp =>
        {
            var navigationManager = sp.GetRequiredService<NavigationManager>();
            var apiClient = sp.GetRequiredService<ApiClientFactory>();
            var mapper = sp.GetRequiredService<IMapper>();

            var viewState = new ViewState();

            var OnLocationChanged = new EventHandler<LocationChangedEventArgs>((object? sender, LocationChangedEventArgs args) =>
            {
                viewState.Reload();
            });

            navigationManager.LocationChanged += OnLocationChanged;

            viewState.DisposeAction = () =>
            {
                navigationManager.LocationChanged -= OnLocationChanged;
            };

            viewState.ReloadAction = () =>
            {
                var pageResponse = apiClient.Page.GetByUrlAsync(navigationManager.Uri).GetAwaiter().GetResult();

                if (pageResponse?.Data == null)
                    throw new Exception("Error while loading ViewState");

                var page = pageResponse.Data.Current;
                page ??= pageResponse.Data.Parent;

                viewState.Page = mapper.Map<PageViewState>(page);
                viewState.Page.Slug = pageResponse.Data.Slug;
                viewState.Layout = mapper.Map<LayoutViewState>(page.Layout);
                viewState.DetailLayout = mapper.Map<LayoutViewState>(page.DetailLayout);
                viewState.Site = mapper.Map<SiteViewState>(page.Site);
                viewState.Plugins = page.Sections!.Values.SelectMany(x => x).Select(p => mapper.Map<PluginViewState>(p)).ToList();
                viewState.User = mapper.Map<UserViewState>(page.User);
                viewState.User.Id = page.User.UserId;

                viewState.Site.HasAdminAccess = viewState.User.IsSuperAdmin || (page.Site.AdminRoleIds ?? []).Any(role => viewState.User?.Roles.Select(x => x.Id).Contains(role) ?? false);
                viewState.Site.HasContributorAccess = viewState.Site.HasAdminAccess || (page.Site.ContributorRoleIds ?? []).Any(role => viewState.User?.Roles.Select(x => x.Id).Contains(role) ?? false);

                viewState.Page.HasAdminAccess = viewState.Site.HasContributorAccess || (page.AdminRoleIds ?? []).Any(role => viewState.User?.Roles.Select(x => x.Id).Contains(role) ?? false);
                viewState.Page.HasViewAccess = viewState.Page.HasAdminAccess || (page.ViewRoleIds ?? []).Any(role => viewState.User?.Roles.Select(x => x.Id).Contains(role) ?? false);

                // check if the page is in edit mode
                // it should have pluginId and pluginViewName query strings
                var uriBuilder = new UriBuilder(navigationManager.Uri);
                var queryParams = HttpUtility.ParseQueryString(uriBuilder.Query);
                if (queryParams["pluginId"] != null && queryParams["viewName"] != null)
                {
                    // check if the pluginId is valid
                    if (Guid.TryParse(queryParams["pluginId"], out var pluginId))
                    {
                        viewState.Type = ViewStateType.PluginDetail;
                        
                        viewState.Plugin = viewState.Plugins.Single(x => x.Id == pluginId);
                        viewState.PluginViewName = queryParams["viewName"];
                    }
                }

                if (queryParams["pageEdit"] != null)
                    viewState.Type = ViewStateType.PageEdit;

                if (queryParams["pagePreview"] != null)
                    viewState.Type = ViewStateType.PagePreview;
            };
            viewState.Reload();

            return viewState;
        });

        return services;
    }
}

