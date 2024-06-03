namespace FluentCMS.Admin.Auth;

public partial class RegisterViewPlugin
{
    private const string FORM_NAME = "RegisterForm";

    [SupplyParameterFromForm(FormName = FORM_NAME)]
    private UserRegisterRequest Model { get; set; } = new();

    private async Task OnSubmit()
    {
        await GetApiClient<AccountClient>().RegisterAsync(Model);
        NavigateTo("/");
    }
}
