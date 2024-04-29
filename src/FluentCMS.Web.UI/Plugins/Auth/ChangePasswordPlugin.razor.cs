using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace FluentCMS.Web.UI.Plugins.Auth;

public partial class ChangePasswordPlugin
{
    private UserChangePasswordRequest Model { get; } = new();

    [CascadingParameter]
    public Task<AuthenticationState> AuthenticationState { get; set; } = default!;

    private async Task OnSubmit()
    {
        Model.UserId = Guid.Parse((await AuthenticationState).User.FindFirstValue(ClaimTypes.Sid)!);
        await GetApiClient<AccountClient>().ChangePasswordAsync(Model);
        NavigateBack();
    }
}
