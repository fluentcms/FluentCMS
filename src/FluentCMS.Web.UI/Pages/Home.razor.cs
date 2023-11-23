
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.WebUtilities;
using System.Xml.Linq;
using System;

namespace FluentCMS.Web.UI.Pages;
public partial class Home
{
    public Guid SiteId = Guid.Empty;
    [Inject] public NavigationManager NavManager { get; set; }
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
