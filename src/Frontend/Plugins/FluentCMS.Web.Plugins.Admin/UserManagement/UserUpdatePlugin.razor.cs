namespace FluentCMS.Web.Plugins.Admin.UserManagement;

public partial class UserUpdatePlugin
{
    public const string FORM_NAME = "UserUpdateForm";
    public const string FORM_NAME_PASSWORD = "UserSetPasswordForm";
    public const string FORM_NAME_ASSIGN_ROLE = "AssignRoleToUser";

    [CascadingParameter]
    private ViewState ViewState { get; set; } = default!;

    [SupplyParameterFromQuery(Name = "id")]
    private Guid Id { get; set; }

    [SupplyParameterFromForm(FormName = FORM_NAME)]
    private UserUpdateRequest? UpdateModel { get; set; }

    [SupplyParameterFromForm(FormName = FORM_NAME_PASSWORD)]
    private UserSetPasswordRequest? SetPasswordModel { get; set; }

    [SupplyParameterFromForm(FormName = FORM_NAME_ASSIGN_ROLE)]
    private UserRoleUpdateRequest? RoleAssignmentModel { get; set; }

    private List<UserRoleDetailResponse>? Roles { get; set; }

    [SupplyParameterFromForm(Name = "selectedRoleIds")]
    private ICollection<Guid>? SelectedRoleIds { get; set; } = default!;

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

        if (RoleAssignmentModel is null)
        {
            var rolesResponse = await ApiClient.UserRole.GetUserRolesAsync(Id, ViewState.Site.Id);
            Roles = rolesResponse?.Data?.ToList() ?? [];

            SelectedRoleIds = Roles?.Where(x => x.HasAccess).Select(x => x.RoleId).ToList();
            RoleAssignmentModel ??= new UserRoleUpdateRequest() { UserId = Id, SiteId = ViewState.Site.Id };
        }
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

    private async Task OnRoleAssigment()
    {
        RoleAssignmentModel!.RoleIds = SelectedRoleIds;

        await ApiClient.UserRole.UpdateAsync(RoleAssignmentModel);
        NavigateBack();
    }
}
