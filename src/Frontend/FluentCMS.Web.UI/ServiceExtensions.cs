using AutoMapper;
using FluentCMS;
using FluentCMS.Web.UI;
using FluentCMS.Web.UI.DynamicRendering;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Http;
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

        app.MapRobotsTxtRoute();
        app.MapSiteMapXmlRoute();

        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();

        return app;
    }

    public static IApplicationBuilder UseRemoteStaticFileServices(this WebApplication app)
    {
        app.UseWhen(context => context.Request.Path.StartsWithSegments($"/files"), app =>
        {
            app.UseMiddleware<RemoteFileProviderMiddleware>();
        });

        return app;
    }

    private static void MapRobotsTxtRoute(this WebApplication app) {
        app.MapGet("/robots.txt", async (HttpContext httpContext, ApiClientFactory apiClient) => {
            try
            {
                // Extract the domain from the current URI
                var url = httpContext.Request.Scheme + "://" + httpContext.Request.Host;

                // Fetch the robots.txt content for the specific domain
                var robotsTxtResponse = await apiClient.Site.GetRobotsTxtAsync(url);
                var robotsTxtContent = robotsTxtResponse.Data;

                // Return the content as plain text
                return Results.Text(robotsTxtContent, "text/plain");
            }
            catch (Exception ex)
            {
                // Handle any potential errors
                Console.WriteLine($"Error fetching robots.txt for domain: {ex.Message}");
                return Results.Problem("Unable to retrieve robots.txt", statusCode: 500);
            }
        });
    }
    private static void MapSiteMapXmlRoute(this WebApplication app) {
        app.MapGet("/sitemap.xml", async (HttpContext httpContext, ApiClientFactory apiClient) => {
            try
            {
                var url = httpContext.Request.Scheme + "://" + httpContext.Request.Host;

                var sitemapResponse = await apiClient.Site.GetSitemapXmlAsync(url);
                var sitemapContent = sitemapResponse.Data;

                // Return the content as plain text
                return Results.Text(sitemapContent, "text/xml");
            }
            catch (Exception ex)
            {
                // Handle any potential errors
                Console.WriteLine($"Error fetching sitemap for domain: {ex.Message}");
                return Results.Problem("Unable to retrieve sitemap", statusCode: 500);
            }
        });
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

                // For setup page
                page ??= new();
                page.User ??= new();
                page.Site ??= new();
                page.Sections ??= [];

                viewState.Page = mapper.Map<PageViewState>(page);
                viewState.Page.Slug = pageResponse.Data.Slug;
                viewState.Layout = mapper.Map<LayoutViewState>(page.Layout);
                viewState.DetailLayout = mapper.Map<LayoutViewState>(page.DetailLayout);
                viewState.EditLayout = mapper.Map<LayoutViewState>(page.EditLayout);
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
            };
            viewState.Reload();

            return viewState;
        });

        return services;
    }
}

