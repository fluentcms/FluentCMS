namespace FluentCMS.Web.UI.Plugins.UserManagement;

public partial class UserCreatePlugin
{
    [SupplyParameterFromForm(FormName = FORM_NAME)]
    private UserCreateRequest Model { get; set; } = new();

    const string FORM_NAME = "UserCreateForm";

    private string? Error { get; set; }

    private async Task OnSubmit()
    {
        try
        {
            var apiResult = await GetApiClient<UserClient>().CreateAsync(Model);
            NavigateBack();
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
    }
}
