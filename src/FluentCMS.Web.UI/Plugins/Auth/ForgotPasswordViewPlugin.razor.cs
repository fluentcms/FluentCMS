namespace FluentCMS.Web.UI.Plugins.Auth;

public partial class ForgotPasswordViewPlugin
{
    private UserSendPasswordResetTokenRequest Model { get; } = new();


    private async Task OnSubmit()
    {
        await GetApiClient<AccountClient>().SendPasswordResetTokenAsync(Model);

        NavigationManager.NavigateTo("/auth/reset-password?email=" + Model.Email);
    }
}
