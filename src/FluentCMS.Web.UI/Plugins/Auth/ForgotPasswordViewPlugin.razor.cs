namespace FluentCMS.Web.UI.Plugins.Auth;

public partial class ForgotPasswordViewPlugin
{
    public const string FORM_NAME = "ForgotPasswordForm";

    [SupplyParameterFromForm(FormName = FORM_NAME)]
    private UserSendPasswordResetTokenRequest Model { get; set; } = new();

    private async Task OnSubmit()
    {
        await GetApiClient<AccountClient>().SendPasswordResetTokenAsync(Model);

        NavigateTo("/auth/reset-password?email=" + Model.Email);
    }
}
