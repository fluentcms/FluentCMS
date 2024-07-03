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
            var user = await ApiClient.Account.GetCurrentAsync();
            ProfileModel = Mapper.Map<AccountUpdateRequest>(user.Data);
        }
        ChangePasswordModel ??= new AccountChangePasswordRequest();
    }

    private async Task OnProfileSubmit()
    {
        await ApiClient.Account.UpdateCurrentAsync(ProfileModel);
        NavigateTo("/admin");
    }

    private async Task OnChangePasswordSubmit()
    {
        await ApiClient.Account.ChangePasswordAsync(ChangePasswordModel);
        NavigateTo("/admin");
    }
}
