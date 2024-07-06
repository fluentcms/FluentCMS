namespace FluentCMS.Web.Plugins.Admin.UserManagement;

public partial class UserListPlugin
{
    private List<UserDetailResponse> Users { get; set; } = [];

    protected override async Task OnInitializedAsync()
    {
        var usersResponse = await ApiClient.User.GetAllAsync();
        Users = usersResponse?.Data?.ToList() ?? [];
    }
}
