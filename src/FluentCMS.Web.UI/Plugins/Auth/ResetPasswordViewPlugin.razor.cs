namespace FluentCMS.Web.UI.Plugins.Auth;

public partial class ResetPasswordViewPlugin
{
    public const string FORM_NAME = "ResetPasswordForm";

    [SupplyParameterFromQuery(Name = nameof(Email))]
    public string Email { get; set; } = default!;

    [SupplyParameterFromForm(FormName = FORM_NAME)]
    private UserValidatePasswordResetTokenRequest Model { get; set; } = new();

    protected override void OnInitialized()
    {
        if (string.IsNullOrEmpty(Email))
        {
            throw new ArgumentNullException(nameof(Email));
        }
        Model.Email = Email;
    }

    private async Task OnSubmit()
    {
        await GetApiClient<AccountClient>().ValidatePasswordResetTokenAsync(Model);
        NavigateTo("/auth/login");
    }
}
