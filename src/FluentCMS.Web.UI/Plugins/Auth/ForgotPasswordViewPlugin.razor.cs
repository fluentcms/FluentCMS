namespace FluentCMS.Web.UI.Plugins.Auth;

public partial class ForgotPasswordViewPlugin
{
    public const string FORM_NAME = "ForgotPasswordForm";

    [SupplyParameterFromForm(FormName = FORM_NAME)]
    private UserSendPasswordResetTokenRequest Model { get; set; } = new();

    [SupplyParameterFromQuery(Name = nameof(Sent))]
    public string Sent { get; set; }

    private async Task OnSubmit()
    {
        await GetApiClient<AccountClient>().SendPasswordResetTokenAsync(Model);

        NavigateTo("/auth/forgot-password?sent=true");
    }
}
