namespace FluentCMS.Web.UI.Plugins.Auth;

public partial class LoginViewPlugin
{
    [Inject]
    private NavigationManager NavigationManager { get; set; } = default!;

    private UserLoginRequest Model { get; set; } = new();

    private async Task OnSubmit()
    {
    }
}
