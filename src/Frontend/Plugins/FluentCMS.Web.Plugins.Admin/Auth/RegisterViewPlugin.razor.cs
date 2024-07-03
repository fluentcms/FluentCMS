namespace FluentCMS.Web.Plugins.Admin.Auth;

public partial class RegisterViewPlugin
{
    public const string FORM_NAME = "RegisterForm";

    [SupplyParameterFromForm(FormName = FORM_NAME)]
    private UserRegisterRequest Model { get; set; } = new();

    private async Task OnSubmit()
    {
        await ApiClient.Account.RegisterAsync(Model);
        NavigateTo("/");
    }
}
