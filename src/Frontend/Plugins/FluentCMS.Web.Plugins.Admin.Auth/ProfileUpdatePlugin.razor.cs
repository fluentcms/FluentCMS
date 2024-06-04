namespace FluentCMS.Web.Plugins.Admin.Auth;

public partial class ProfileUpdatePlugin
{
    public const string PROFILE_FORM_NAME = "ProfileUpdateForm";

    public const string CHANGE_PASSWORD_FORM_NAME = "ChangePasswordForm";

    [SupplyParameterFromForm(FormName = PROFILE_FORM_NAME)]
    private AccountUpdateRequest? ProfileModel { get; set; }

    [SupplyParameterFromForm(FormName = CHANGE_PASSWORD_FORM_NAME)]
    private AccountChangePasswordRequest? ChangePasswordModel { get; set; }

    protected override async Task OnInitializedAsync()
    {
        if (ProfileModel == null)
        {
            var user = await GetApiClient<AccountClient>().GetCurrentAsync();
            ProfileModel = Mapper.Map<AccountUpdateRequest>(user.Data);
        }
        ChangePasswordModel ??= new AccountChangePasswordRequest();
    }

    private async Task OnProfileSubmit()
    {
        await GetApiClient<AccountClient>().UpdateCurrentAsync(ProfileModel);
        NavigateTo("/admin");
    }

    private async Task OnChangePasswordSubmit()
    {
        await GetApiClient<AccountClient>().ChangePasswordAsync(ChangePasswordModel);
        NavigateTo("/admin");
    }
}
