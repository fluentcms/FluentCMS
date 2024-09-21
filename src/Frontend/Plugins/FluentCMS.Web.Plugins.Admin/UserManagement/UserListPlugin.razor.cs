namespace FluentCMS.Web.Plugins.Admin.UserManagement;

public partial class UserListPlugin
{
    private List<UserDetailResponse> Users { get; set; } = [];

    public async Task Load()
    {
        var usersResponse = await ApiClient.User.GetAllAsync();
        Users = usersResponse?.Data?.ToList() ?? [];
    }

    protected override async Task OnInitializedAsync()
    {
        await Load();
    }


    #region Delete User

    private UserDetailResponse? SelectedUser { get; set; }
    public async Task OnDelete()
    {
        if (SelectedUser == null)
            return;

        await ApiClient.User.DeleteAsync(SelectedUser.Id);
        await Load();
        SelectedUser = default;
    }

    public async Task OnConfirm(UserDetailResponse user)
    {
        SelectedUser = user;
        await Task.CompletedTask;
    }
    public async Task OnConfirmClose()
    {
        SelectedUser = default;
        await Task.CompletedTask;
    }

    #endregion
}
