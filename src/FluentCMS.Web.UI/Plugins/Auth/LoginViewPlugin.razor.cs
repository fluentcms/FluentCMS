namespace FluentCMS.Web.UI.Plugins.Auth;

public partial class LoginViewPlugin
{
    public const string FORM_NAME = "LoginForm";

    [Inject]
    private AuthManager AuthManager { get; set; } = default!;

    [CascadingParameter]
    private HttpContext HttpContext { get; set; } = default!;

    [SupplyParameterFromForm(FormName = FORM_NAME)]
    private UserLoginRequest Model { get; set; } = new();
    private async Task OnSubmit()
    {
        await AuthManager.Login(HttpContext, Model.Username, Model.Password, Model.RememberMe);
        NavigateTo("/");
    }
}
