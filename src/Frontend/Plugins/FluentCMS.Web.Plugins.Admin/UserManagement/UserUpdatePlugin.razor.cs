namespace FluentCMS.Web.Plugins.Admin.UserManagement;

public partial class UserUpdatePlugin
{
    public const string FORM_NAME = "UserUpdateForm";
    public const string FORM_NAME_PASSWORD = "UserSetPasswordForm";

    [SupplyParameterFromQuery(Name = "id")]
    private Guid Id { get; set; }

    [SupplyParameterFromForm(FormName = FORM_NAME)]
    private UserUpdateRequest? UpdateModel { get; set; }


    [SupplyParameterFromForm(FormName = FORM_NAME_PASSWORD)]
    private UserSetPasswordRequest? SetPasswordModel { get; set; }

    private List<RoleDetailResponse>? Roles { get; set; }

    private string? Username { get; set; }

    protected override async Task OnInitializedAsync()
    {
        if (Roles is null)
        {
            var rolesResponse = await ApiClient.Role.GetAllAsync();
            Roles = rolesResponse?.Data?.ToList() ?? [];
        }

        if (UpdateModel is null)
        {
            var userResponse = await ApiClient.User.GetAsync(Id);
            var user = userResponse.Data;
            Username = user.Username;
            UpdateModel = Mapper.Map<UserUpdateRequest>(user);
            UpdateModel.RoleIds = user.Roles?.Select(r => r.Id).ToList() ?? [];
        }

        SetPasswordModel ??= new UserSetPasswordRequest() { UserId = Id };
    }

    private async Task OnSubmit()
    {
        UpdateModel!.RoleIds ??= [];
        await ApiClient.User.UpdateAsync(UpdateModel);
        NavigateBack();
    }

    private async Task OnChangePassword()
    {
        await ApiClient.User.SetPasswordAsync(SetPasswordModel);
        NavigateBack();
    }
}
