using FluentCMS.Web.UI.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace FluentCMS.Web.UI.Plugins.Auth;

public partial class ProfileViewPlugin
{
    [Inject] public AccountClient AccountClient { get; set; } = default!;
    

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        await base.OnAfterRenderAsync(firstRender);
        View = await AccountClient.GetDetails();
        //StateHasChanged();
    }
}
