namespace FluentCMS.Web.Plugins.Admin.UserManagement;

public partial class UserListPlugin
{
    private List<UserDetailResponse> Users { get; set; } = [];

    [CascadingParameter]
    private ViewState ViewState { get; set; } = default!;

    public UserDetailResponse SelectedItem { get; set; } = default;

    private bool UserRoleAssignmentModalOpen { get; set; } = false;

    private List<UserRoleDetailResponse>? Roles { get; set; }

    protected override async Task OnInitializedAsync()
    {
        var usersResponse = await ApiClient.User.GetAllAsync();
        Users = usersResponse?.Data?.ToList() ?? [];
    }

    private async Task OpenRoleAssignment(UserDetailResponse user)
    {
        SelectedItem = user;

        var rolesResponse = await ApiClient.UserRole.GetUserRolesAsync(user.Id, ViewState.Site.Id);
        Roles = rolesResponse?.Data?.ToList() ?? [];

        UserRoleAssignmentModalOpen = true;
    }

    private async Task OnRoleAssignmentSubmit(UserRoleUpdateRequest userRole)
    {
        await ApiClient.UserRole.UpdateAsync(userRole);
        UserRoleAssignmentModalOpen = false;
    }

    private async Task OnRoleAssignmentCancel()
    {
        UserRoleAssignmentModalOpen = false;
    }
}
