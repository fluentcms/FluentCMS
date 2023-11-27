
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.WebUtilities;
using System.Xml.Linq;
using System;

namespace FluentCMS.Web.UI.Pages;
public partial class Home
{
    public Guid SiteId = Guid.Empty;
    [Inject] public NavigationManager NavManager { get; set; }
    public string? AccessToken{ get; set; } = "";
    public Guid? UserId{ get; set; } = Guid.Empty;
    public Guid[]? RoleIds{ get; set; } = [];
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
            var a = await AuthenticationStateProvider.GetAuthenticationStateAsync();
            AccessToken = (await ProtectedLocalStorage.GetAsync<string>("access-token")).Value;
            UserId = (await ProtectedLocalStorage.GetAsync<Guid>("user-id")).Value;
            RoleIds = (await ProtectedLocalStorage.GetAsync<Guid[]>("role-ids")).Value;
            StateHasChanged();
        }

    }
}
