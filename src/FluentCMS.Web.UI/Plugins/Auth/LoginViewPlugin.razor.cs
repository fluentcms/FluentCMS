using FluentCMS.Web.UI.Services;

namespace FluentCMS.Web.UI.Plugins.Auth;

public partial class LoginViewPlugin
{
    [Inject]
    private AuthStateProvider AuthStateProvider { get; set; } = default!;

    [Inject]
    private NavigationManager NavigationManager { get; set; } = default!;

    private UserLoginRequest Model { get; set; } = new();

    private async Task OnSubmit()
    {
        var result = await AuthStateProvider.Login(Model);
        if (result.Errors!.Count == 0)
        {
            NavigationManager.NavigateTo("/", true);
        }
    }
}
