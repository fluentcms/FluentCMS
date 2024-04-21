namespace FluentCMS.Web.UI.Plugins.UserManagement;

public partial class UserCreatePlugin
{
    [SupplyParameterFromForm(FormName = FORM_NAME)]
    private UserCreateRequest Model { get; set; } = new();

    const string FORM_NAME = "UserCreateForm";

    private async Task OnSubmit()
    {
        var apiResult = await HttpClientFactory.GetClient<UserClient>().CreateAsync(Model);
        if (apiResult.IsSuccess)
        {
            NavigationManager.NavigateTo("/");
        }
        else
        {

        }
    }
}
