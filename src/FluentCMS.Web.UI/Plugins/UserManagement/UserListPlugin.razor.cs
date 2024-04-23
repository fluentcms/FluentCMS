using Microsoft.AspNetCore.Components.Authorization;

namespace FluentCMS.Web.UI.Plugins.UserManagement;

public partial class UserListPlugin
{
    private List<UserDetailResponse> Users { get; set; } = [];

    [Inject]
    public UserClient UserClient { get; set; } = default!;

    [Inject]
    public AuthenticationStateProvider AuthenticationStateProvider { get; set; } = default!;
    public AuthenticationState? UserDetail { get; private set; }

    protected override async Task OnLoadAsync()
    {
        UserDetail = await AuthenticationStateProvider.GetAuthenticationStateAsync();

        AuthenticationStateProvider.AuthenticationStateChanged += async (state) =>
        {
            UserDetail = await state;
        };

        var usersResponse = await UserClient.GetAllAsync();
        Users = usersResponse?.Data?.ToList() ?? [];
    }
}
