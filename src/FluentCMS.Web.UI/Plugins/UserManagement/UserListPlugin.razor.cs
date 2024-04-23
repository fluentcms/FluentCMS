namespace FluentCMS.Web.UI.Plugins.UserManagement;

public partial class UserListPlugin
{
    private List<UserDetailResponse> Users { get; set; } = [];

    [Inject]
    public UserClient UserClient { get; set; } = default!;

    protected override async Task OnLoadAsync()
    {
        var usersResponse = await UserClient.GetAllAsync();
        Users = usersResponse?.Data?.ToList() ?? [];
    }
}
