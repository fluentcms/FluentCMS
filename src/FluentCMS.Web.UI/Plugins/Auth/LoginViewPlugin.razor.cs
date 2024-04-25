using Microsoft.AspNetCore.Components;

namespace FluentCMS.Web.UI.Plugins.Auth;

public partial class LoginViewPlugin
{

    private UserLoginRequest Model { get; set; } = new();

    private async Task OnSubmit()
    {
    }
}
