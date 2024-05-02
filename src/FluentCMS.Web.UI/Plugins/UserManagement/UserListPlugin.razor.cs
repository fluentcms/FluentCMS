namespace FluentCMS.Web.UI.Plugins.UserManagement;

public partial class UserListPlugin
{
    private List<UserDetailResponse> Users { get; set; } = [];

    protected override async Task OnLoadAsync()
    {
        var usersResponse = await GetApiClient<UserClient>().GetAllAsync();
        Users = usersResponse?.Data?.ToList() ?? [];
    }
}
