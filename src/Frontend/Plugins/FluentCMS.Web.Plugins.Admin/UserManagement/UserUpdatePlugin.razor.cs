namespace FluentCMS.Web.Plugins.Admin.UserManagement;

public partial class UserUpdatePlugin
{
    public const string FORM_NAME = "UserUpdateForm";
    public const string FORM_NAME_PASSWORD = "UserSetPasswordForm";
    public const string FORM_NAME_ASSIGN_ROLE = "AssignRoleToUser";

    [SupplyParameterFromQuery(Name = "id")]
    private Guid Id { get; set; }

    [SupplyParameterFromForm(FormName = FORM_NAME)]
    private UserUpdateRequest? UpdateModel { get; set; }

    [SupplyParameterFromForm(FormName = FORM_NAME_PASSWORD)]
    private UserSetPasswordRequest? SetPasswordModel { get; set; }

    [SupplyParameterFromForm(FormName = FORM_NAME_ASSIGN_ROLE)]
    private UserRoleUpdateRequest? UserRolesModel { get; set; }

    private string? Username { get; set; }

    protected override async Task OnInitializedAsync()
    {
        if (UpdateModel is null)
        {
            var userResponse = await ApiClient.User.GetAsync(Id);
            var user = userResponse.Data;
            Username = user.Username;
            UpdateModel = Mapper.Map<UserUpdateRequest>(user);
        }

        SetPasswordModel ??= new UserSetPasswordRequest() { UserId = Id };

        if (UserRolesModel is null)
        {
            var userRoles = await ApiClient.UserRole.GetUserRolesAsync(Id, ViewState.Site.Id);
            UserRolesModel = new UserRoleUpdateRequest
            {
                UserId = Id,
                SiteId = ViewState.Site.Id,
                RoleIds = userRoles.Data?.Select(x => x.Id).ToList() ?? []
            };
        }
    }

    private async Task OnUserUpdate()
    {
        await ApiClient.User.UpdateAsync(UpdateModel);
        NavigateBack();
    }

    private async Task OnChangePassword()
    {
        await ApiClient.User.SetPasswordAsync(SetPasswordModel);
        NavigateBack();
    }

    private async Task OnRoleUpdate()
    {
        UserRolesModel!.RoleIds ??= [];
        await ApiClient.UserRole.UpdateAsync(UserRolesModel);
        NavigateBack();
    }
}
