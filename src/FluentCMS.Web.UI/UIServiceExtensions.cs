using FluentCMS.Api.Models;
using Microsoft.AspNetCore.Components;
using System.Web;
using FluentCMS.Web.UI;

namespace Microsoft.Extensions.DependencyInjection;

public static class UIServiceExtensions
{

    public static IServiceCollection AddUIServices(this IServiceCollection services)
    {
        // Add services to the container.
        services.AddRazorComponents()
            .AddInteractiveServerComponents();

        services.AddCascadingValue(sp =>
        {
            var appState = new AppState();

            var navigation = sp.GetRequiredService<NavigationManager>();
            var http = sp.GetRequiredService<HttpClient>();

            var host = navigation.BaseUri;
            if (host.EndsWith("/"))
                host = host.Substring(0, host.Length - 1);

            var siteResult = http.GetFromJsonAsync<ApiResult<SiteResponse>>($"Site/GetByUrl?url={host}").GetAwaiter().GetResult();
            appState.Site = siteResult?.Data;
            appState.Layout = appState.Site?.Layout;

            if (appState.Site != null)
            {
                var query = HttpUtility.ParseQueryString(string.Empty);
                query["siteId"] = appState.Site.Id.ToString();
                query["path"] = "";

                var pageResult = http.GetFromJsonAsync<ApiResult<PageResponse>>($"Page/GetByPath?{query}").GetAwaiter().GetResult();
                appState.Page = pageResult?.Data;
                if (appState.Page != null && appState.Page.Layout != null)
                    appState.Layout = appState.Page.Layout;
            }
            return appState;
        });

        return services;
    }
}
