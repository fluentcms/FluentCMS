namespace FluentCMS.Web.UI.Plugins.Auth;
public partial class ChangePasswordPlugin
{
    private UserChangePasswordRequest Model { get; set; } = new();
    public async Task OnSumbit()
    {
        await GetApiClient<AccountClient>().ChangePasswordAsync(Model);
        NavigateBack();
    }
}
