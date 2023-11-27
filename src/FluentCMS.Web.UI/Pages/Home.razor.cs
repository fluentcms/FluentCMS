
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.WebUtilities;
using System.Xml.Linq;
using System;
using System.Text.Json;

namespace FluentCMS.Web.UI.Pages;
public partial class Home
{
    public Guid SiteId = Guid.Empty;
    [Inject] public NavigationManager NavManager { get; set; }
    public string? AccessToken{ get; set; } = "";
    public Guid? UserId{ get; set; } = Guid.Empty;
    public Guid[]? RoleIds{ get; set; } = [];
    protected override void OnInitialized()
    {
        base.OnInitialized();
        AccessToken = HttpContextAccessor.HttpContext!.Request.Cookies["access-token"];
        UserId = JsonSerializer.Deserialize<Guid?>(HttpContextAccessor.HttpContext!.Request.Cookies["user-id"]??"null");
        RoleIds = JsonSerializer.Deserialize<Guid[]?>(HttpContextAccessor.HttpContext!.Request.Cookies["role-ids"]?? "null");
    }
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        await base.OnAfterRenderAsync(firstRender);
        
        if (firstRender)
        {
            var uri = NavManager.ToAbsoluteUri(NavManager.Uri);
            var queryStrings = QueryHelpers.ParseQuery(uri.Query);
            if (queryStrings.TryGetValue("site-id", out var siteId))
            {
                SiteId = Guid.Parse(siteId);
            }

            StateHasChanged();
        }

    }
}
