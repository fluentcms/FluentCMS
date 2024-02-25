using FluentCMS.Web.UI.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace FluentCMS.Web.UI.Plugins.Auth;

public partial class UserInfoViewPlugin
{
    public AuthenticationState? Model { get; set; } = default;
    [Inject] public AuthStateProvider AuthStateProvider { get; set; }
    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        AuthStateProvider.AuthenticationStateChanged += AuthStateProviderOnAuthenticationStateChanged;

        void AuthStateProviderOnAuthenticationStateChanged(Task<AuthenticationState> task)
        {
            Console.WriteLine(task.Result.User.Identity.IsAuthenticated);
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        await base.OnAfterRenderAsync(firstRender);
        Model = await AuthStateProvider.GetAuthenticationStateAsync();
        StateHasChanged();
    }
}
