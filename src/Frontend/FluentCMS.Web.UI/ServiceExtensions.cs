using AutoMapper;
using FluentCMS;
using FluentCMS.Web.UI;
using FluentCMS.Web.UI.DynamicRendering;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
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

        // add global cascade parameter for Page (PageFullDetailResponse)
        // if we use cascading value in the component, it will be null in component which are rendered as InteractiveMode
        // because the page is SSR and the cascading value is not available in the component
        // so we set the cascading value globally in the service
        // https://github.com/dotnet/aspnetcore/issues/53482
        services.AddCascadingValue(sp =>
        {
            var viewState = new ViewState();
            viewState.Load(sp);
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

