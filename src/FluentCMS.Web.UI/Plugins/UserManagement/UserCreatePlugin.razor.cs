namespace FluentCMS.Web.UI.Plugins.UserManagement;

public partial class UserCreatePlugin
{
    public const string FORM_NAME = "UserCreateForm";

    [SupplyParameterFromForm(FormName = FORM_NAME)]
    private UserCreateRequest Model { get; set; } = new();

    private List<RoleDetailResponse> Roles { get; set; } = [];

    protected override async Task OnLoadAsync()
    {
        var rolesResponse = await GetApiClient<RoleClient>().GetAllAsync();
        Roles = rolesResponse?.Data?.ToList() ?? [];
    }

    private async Task OnSubmit()
    {
        Model.RoleIds ??= [];
        await GetApiClient<UserClient>().CreateAsync(Model);
        NavigateBack();
    }
}
