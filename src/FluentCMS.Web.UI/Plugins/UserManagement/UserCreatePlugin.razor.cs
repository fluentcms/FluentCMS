namespace FluentCMS.Web.UI.Plugins.UserManagement;

public partial class UserCreatePlugin
{
    private UserCreateRequest Model { get; set; } = new() { Enabled = true };

    private async Task OnSubmit()
    {
        await GetApiClient<UserClient>().CreateAsync(Model);
        NavigateBack();
    }
}
