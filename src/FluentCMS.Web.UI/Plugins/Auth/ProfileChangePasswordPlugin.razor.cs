using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace FluentCMS.Web.UI.Plugins.Auth;

public partial class ProfileChangePasswordPlugin
{
    [CascadingParameter]
    public Task<AuthenticationState> UserState { get; set; } = default!;

    private UserChangePasswordRequest Model { get; set; } = new()
    {
        UserId = Guid.Empty,
        NewPassword = "",
        OldPassword = ""
    };

    protected override async Task OnLoadAsync()
    {
        await base.OnLoadAsync();
        Model.UserId = Guid.Parse((await UserState).User.FindFirstValue(ClaimTypes.Sid)!);
    }

    private async Task OnSubmit()
    {
        await GetApiClient<AccountClient>().ChangePasswordAsync(Model);
        NavigateBack();
    }
}
