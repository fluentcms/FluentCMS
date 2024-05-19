namespace FluentCMS.Web.UI.Plugins.UserManagement;

public partial class UserCreatePlugin
{
    [SupplyParameterFromForm(FormName = FORM_NAME)]
    private UserCreateRequest Model { get; set; } = new();

    [SupplyParameterFromForm(FormName = FORM_NAME)]
    private ICollection<Guid> RoleIds { get; set; } = [];

    private List<RoleDetailResponse> Roles { get; set; } = [];
    

    public const string FORM_NAME = "UserCreateForm";

    protected override async Task OnLoadAsync()
    {
        var rolesResponse = await GetApiClient<RoleClient>().GetAllAsync();
        Roles = rolesResponse?.Data?.ToList() ?? [];
    }

    private async Task OnSubmit()
    {
        await GetApiClient<UserClient>().CreateAsync(Model);
        NavigateBack();
    }
}
