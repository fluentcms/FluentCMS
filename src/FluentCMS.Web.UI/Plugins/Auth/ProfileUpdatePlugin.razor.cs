namespace FluentCMS.Web.UI.Plugins.Auth;

public partial class ProfileUpdatePlugin
{
    public const string PROFILE_FORM_NAME = "ProfileUpdateForm";

    public const string CHANGE_PASSWORD_FORM_NAME = "ChangePasswordForm";

    [SupplyParameterFromForm(FormName = PROFILE_FORM_NAME)]
    private AccountUpdateRequest ProfileModel { get; set; } = new();

    [SupplyParameterFromForm(FormName = CHANGE_PASSWORD_FORM_NAME)]
    private UserChangePasswordRequest ChangePasswordModel { get; set; } = new();

    protected override async Task OnInitializedAsync()
    {
        // TODO
        // var user = await GetApiClient<AccountClient>().GetCurrentAsync();
        // Model = Mapper.Map<UserUpdateRequest>(user.Data);
    }

    private async Task OnProfileSubmit()
    {
        await GetApiClient<AccountClient>().UpdateCurrentAsync(ProfileModel);
    }

    private async Task OnChangePasswordSubmit()
    {
        await GetApiClient<AccountClient>().ChangePasswordAsync(ChangePasswordModel);
    }
}
