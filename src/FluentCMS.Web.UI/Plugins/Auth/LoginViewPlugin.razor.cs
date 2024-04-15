using FluentCMS.Web.UI.Services;
using FluentCMS.Web.UI.Services.LocalStorage;
using Microsoft.AspNetCore.Components;

namespace FluentCMS.Web.UI.Plugins.Auth;

public partial class LoginViewPlugin
{
    [Inject]
    private AuthStateProvider AuthStateProvider { get; set; } = default!;

    [Inject]
    private NavigationManager NavigationManager { get; set; } = default!;

    [Inject]
    private ILocalStorageService LocalStorageService { get; set; } = default!;

    private UserLoginRequest Model { get; set; } = new();

    private async Task OnSubmit()
    {
        var result = await AuthStateProvider.LoginAsync(Model);
        if (result.Errors!.Count == 0)
        {
            NavigationManager.NavigateTo("/", true);
        }
    }
}
