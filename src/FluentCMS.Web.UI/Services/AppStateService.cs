using FluentCMS.Api.Models;
using FluentCMS.Entities;
using FluentCMS.Web.UI.ApiClients;
using Microsoft.AspNetCore.Components;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Web;

namespace FluentCMS.Web.UI.Services;

public class AppStateService
{
    public AppState AppState { get; set; } = new();

    public AppStateService(IHttpClientFactory httpClientFactory, NavigationManager Navigator, SiteClient siteClient, PageClient pageClient)
    {
        var HttpClient = httpClientFactory.CreateClient("XXXX");
        // TODO: Move this to configuration
        HttpClient.BaseAddress = new Uri("https://localhost:7164/api/");
        HttpClient.DefaultRequestHeaders.Accept.Clear();
        HttpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        var url = Navigator.BaseUri.EndsWith("/") ? Navigator.BaseUri.Remove(Navigator.BaseUri.Length - 1) : Navigator.BaseUri;
        var query = HttpUtility.ParseQueryString(string.Empty);
        query["url"] = url;
        var x = HttpClient.GetFromJsonAsync<ApiResult<SiteResponse>>("Site/GetByUrl?" + query).GetAwaiter().GetResult();

        AppState ??= new AppState();
        AppState.Host = Navigator.BaseUri.EndsWith("/") ? Navigator.BaseUri.Remove(Navigator.BaseUri.Length - 1) : Navigator.BaseUri;
        AppState.Uri = new Uri(Navigator.Uri);
        AppState.Site = x.Data; // siteClient.GetByUrl(AppState.Host).GetAwaiter().GetResult();
        AppState.Layout = AppState.Site?.Layout;

        //if (AppState.Site != null)
        //    AppState.Page = pageClient.GetByPath(AppState.Site.Id, AppState.Uri.LocalPath).GetAwaiter().GetResult(); ;

        //if (AppState.Page != null && AppState.Page.Layout != null)
        //    AppState.Layout = AppState.Page.Layout;

        //AppState.PluginId = PluginId;
        //AppState.ViewMode = ViewMode;
    }
}
