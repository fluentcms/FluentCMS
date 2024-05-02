namespace FluentCMS.Web.UI.Plugins.UserManagement;

public partial class UserCreatePlugin
{
    [SupplyParameterFromForm(FormName = FORM_NAME)]
    private UserCreateRequest Model { get; set; } = new();

    public const string FORM_NAME = "UserCreateForm";

    private async Task OnSubmit()
    {
        await GetApiClient<UserClient>().CreateAsync(Model);
        NavigateBack();
    }
}
