namespace FluentCMS.Web.UI.Plugins.UserManagement;

public partial class UserCreatePlugin
{
    private UserCreateRequest Model { get; set; } = new();
    private async Task OnSubmit()
    {
        await GetApiClient<UserClient>().CreateAsync(Model);
        NavigateBack();
    }
}
