
namespace FluentCMS.Web.UI.Plugins.UserManagement;

public partial class UserListPlugin
{
    private List<UserDetailResponse> Users { get; set; } = [];

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        var usersResponse = await GetApiClient<UserClient>().GetAllAsync();
        Users = usersResponse?.Data?.ToList() ?? [];
    }
}
