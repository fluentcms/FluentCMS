using FluentCMS.Web.UI.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;

namespace FluentCMS.Web.UI.Plugins.Auth;

public partial class LoginViewPlugin
{
    public const string FORM_NAME = "LoginForm";

    [Inject]
    private IAuthService AuthService { get; set; } = default!;

    [CascadingParameter]
    protected HttpContext HttpContext { get; set; } = default!;

    [SupplyParameterFromForm(FormName = FORM_NAME)]
    private UserLoginRequest Model { get; set; } = new();

    private async Task OnSubmit()
    {
        await AuthService.Login(HttpContext, Model.Username, Model.Password, Model.Presist);
    }
}
