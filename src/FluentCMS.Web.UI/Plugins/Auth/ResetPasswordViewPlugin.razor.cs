namespace FluentCMS.Web.UI.Plugins.Auth;

public partial class ResetPasswordViewPlugin
{
    [SupplyParameterFromQuery(Name = nameof(Email))]
    public string? Email { get; set; }

    public UserValidatePasswordResetTokenRequest Model { get; } = new();

    protected override async Task OnLoadAsync()
    {
        await base.OnLoadAsync();

        if (string.IsNullOrEmpty(Email))
        {
            throw new ArgumentNullException(nameof(Email));
        }

        Model.Email = Email;
    }

    private async Task OnSubmit()
    {
        await GetApiClient<AccountClient>().ValidatePasswordResetTokenAsync(Model);

        NavigationManager.NavigateTo("/auth/login", true);
    }
}
