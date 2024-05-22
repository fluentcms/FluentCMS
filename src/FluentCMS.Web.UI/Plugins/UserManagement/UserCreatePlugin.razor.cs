namespace FluentCMS.Web.UI.Plugins.UserManagement;

public partial class UserCreatePlugin
{
    public const string FORM_NAME = "UserCreateForm";

    [CascadingParameter]
    protected HttpContext HttpContext { get; set; } = default!;

    [SupplyParameterFromForm(FormName = FORM_NAME)]
    private UserCreateRequest Model { get; set; } = new();

    private List<RoleDetailResponse> Roles { get; set; } = [];

    protected override async Task OnLoadAsync()
    {
        var rolesResponse = await GetApiClient<RoleClient>().GetAllAsync();
        Roles = rolesResponse?.Data?.ToList() ?? [];
        Model.RoleIds = [];
    }

    private async Task OnSubmit()
    {
        await GetApiClient<UserClient>().CreateAsync(Model);
        HttpContext.Response.Redirect("/admin/users");
    }
}
