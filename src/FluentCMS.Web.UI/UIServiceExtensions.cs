using FluentCMS.Api.Models;
using Microsoft.AspNetCore.Components;
using System.Web;
using FluentCMS.Web.UI;
using Microsoft.AspNetCore.Components.Routing;

namespace Microsoft.Extensions.DependencyInjection;

public static class UIServiceExtensions
{

    public static IServiceCollection AddUIServices(this IServiceCollection services)
    {
        // Add services to the container.
        services.AddRazorComponents()
            .AddInteractiveServerComponents();

        //services.AddCascadingValue(sp =>
        //{
        //    var navigation = sp.GetRequiredService<NavigationManager>();
        //    var http = sp.GetRequiredService<HttpClient>();
        //    var appState = new AppState(http, navigation);
        //    return appState;
        //});

        //services.AddScoped<AppState>();

        //services.AddCascadingValue(sp =>
        //{
        //    var navigation = sp.GetRequiredService<NavigationManager>();

        //    var appState = new AppState
        //    {
        //        Uri = navigation.ToAbsoluteUri(navigation.Uri)
        //    };

        //    var host = navigation.BaseUri;
        //    if (host.EndsWith("/"))
        //        host = host.Substring(0, host.Length - 1);

        //    var http = sp.GetRequiredService<HttpClient>();

        //    var siteResult = http.GetFromJsonAsync<ApiResult<SiteResponse>>($"Site/GetByUrl?url={host}").GetAwaiter().GetResult();
        //    appState.Site = siteResult?.Data;
        //    appState.Layout = appState.Site?.Layout;

        //    if (appState.Site != null)
        //    {
        //        var query = HttpUtility.ParseQueryString(string.Empty);
        //        query["siteId"] = appState.Site.Id.ToString();
        //        query["path"] = appState.Uri.LocalPath;

        //        var pageResult = http.GetFromJsonAsync<ApiResult<PageResponse>>($"Page/GetByPath?{query}").GetAwaiter().GetResult();
        //        appState.Page = pageResult?.Data;
        //        if (appState.Page != null && appState.Page.Layout != null)
        //            appState.Layout = appState.Page.Layout;
        //    }
        //    return appState;
        //});

        //services.AddTransient<AppNavigation>();

        return services;
    }
}
//public class AppNavigation:IDisposable
//{
//    private readonly NavigationManager _navigationManager;

//    public AppNavigation(NavigationManager navigationManager)
//    {        
//        _navigationManager = navigationManager;
//        _navigationManager.LocationChanged += OnLocationChanged;
//    }
//    public void OnLocationChanged(object sender, LocationChangedEventArgs e)
//    {
//        string navigationMethod = e.IsNavigationIntercepted ? "HTML" : "code";
//        System.Diagnostics.Debug.WriteLine($"Notified of navigation via {navigationMethod} to {e.Location}");
//    }

//    public void Dispose()
//    {
//        _navigationManager.LocationChanged -= OnLocationChanged;
//    }
//}
