namespace FluentCMS.Web.UI.Plugins.UserManagement;

public partial class UserCreatePlugin
{
    [SupplyParameterFromForm(FormName = FORM_NAME)]
    private UserCreateRequest Model { get; set; } = new();

    [CascadingParameter]
    protected HttpContext HttpContext { get; set; } = default!;

    public const string FORM_NAME = "UserCreateForm";

    private async Task OnSubmit()
    {
        Model.RoleIds = [];
        await GetApiClient<UserClient>().CreateAsync(Model);

        // TODO: User Helper
        HttpContext.Response.Redirect("/admin/users");
    }
}
