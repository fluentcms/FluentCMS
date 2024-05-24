namespace FluentCMS.Web.UI.Plugins.Auth;

public partial class ResetPasswordViewPlugin
{
    [SupplyParameterFromQuery(Name = nameof(Email))]
    public string Email { get; set; } = default!;

    private UserValidatePasswordResetTokenRequest Model { get; set; } = new();

    private async Task OnSubmit()
    {
        await GetApiClient<AccountClient>().ValidatePasswordResetTokenAsync(Model);

        NavigationManager.NavigateTo("/auth/login", true);
    }

    protected override async Task OnInitializedAsync()
    {
        if (string.IsNullOrEmpty(Email))
        {
            throw new ArgumentNullException(nameof(Email));
        }
        Model.Email = Email;
    }
}
