namespace FluentCMS.Web.UI.Plugins.Auth;

public partial class ForgotPasswordViewPlugin
{
    public const string FORM_NAME = "ForgotPasswordForm";

    [SupplyParameterFromForm(FormName = FORM_NAME)]
    private UserSendPasswordResetTokenRequest? Model { get; set; }

    private bool IsPosted { get; set; }

    protected override Task OnInitializedAsync()
    {
        IsPosted = false;
        Model ??= new();
        return base.OnInitializedAsync();
    }

    private async Task OnSubmit()
    {
        await GetApiClient<AccountClient>().SendPasswordResetTokenAsync(Model);
        IsPosted = true;
    }
}
