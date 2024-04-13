using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;

namespace FluentCMS.Web.UI.Plugins.Auth;

public partial class LoginViewPlugin
{
    [Inject] private NavigationManager NavigationManager { get; set; } = default!;

    [CascadingParameter] private HttpContext HttpContext { get; set; }
    [SupplyParameterFromForm(FormName = "LoginForm")]
    private UserLoginRequest Model { get; set; } = new();

    private async Task OnSubmit()
    {
        await HttpContext.SignInAsync(Model);
        NavigationManager.NavigateTo("/", true);
    }
}
