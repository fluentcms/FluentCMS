using FluentCMS.Api.Models;
using FluentCMS.Entities;
using System.Web;
using Microsoft.AspNetCore.Components;

namespace FluentCMS.Web.UI;

public class AppState
{
    private readonly HttpClient _http;
    
    public SiteResponse? Site { get; set; }
    public PageResponse? Page { get; set; }
    public Layout? Layout { get; set; }
    public string? Route { get; set; }
    public Uri? Uri { get; set; }
    public string Host { get; set; }

    public AppState(HttpClient http, NavigationManager navigation)
    {
        Host = navigation.BaseUri;
        if (Host.EndsWith("/"))
            Host = Host.Substring(0, Host.Length - 1);
        _http = http;
    }

    public async Task OnNavigateAsync(string? path)
    {
        if (path == null)
            return;

        Route = path;

        var siteResult = await _http.GetFromJsonAsync<ApiResult<SiteResponse>>($"Site/GetByUrl?url={Host}");
        Site = siteResult?.Data;
        Layout = Site?.Layout;

        if (Site != null)
        {
            var query = HttpUtility.ParseQueryString(string.Empty);
            query["siteId"] = Site.Id.ToString();
            query["path"] = Route;

            var pageResult = await _http.GetFromJsonAsync<ApiResult<PageResponse>>($"Page/GetByPath?{query}");
            Page = pageResult?.Data;
            if (Page != null && Page.Layout != null)
                Layout = Page.Layout;
        }
    }
}
