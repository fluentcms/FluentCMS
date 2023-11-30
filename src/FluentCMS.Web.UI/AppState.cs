using FluentCMS.Api.Models;
using FluentCMS.Entities;
using Microsoft.AspNetCore.Components;

namespace FluentCMS.Web.UI;

public class AppState
{
    public SiteResponse? Site { get; set; }
    public PageResponse? Page { get; set; }
    public Layout? Layout { get; set; }
    public string? Route { get; set; }
    public Uri? Uri { get; set; }
    public string Host { get; set; }

    public AppState()
    {

    }

    public AppState(NavigationManager navigation)
    {
        Host = navigation.BaseUri;
        if (Host.EndsWith("/"))
            Host = Host.Substring(0, Host.Length - 1);
    }
}
