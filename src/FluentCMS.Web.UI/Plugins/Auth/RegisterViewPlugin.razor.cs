namespace FluentCMS.Web.UI.Plugins.Auth;

public partial class RegisterViewPlugin
{
    private UserRegisterRequest Model { get; } = new();

    private async Task OnSubmit()
    {
        await GetApiClient<AccountClient>().RegisterAsync(Model);

        NavigationManager.NavigateTo("/auth/login", true);
    }
}
