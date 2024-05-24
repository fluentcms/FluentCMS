namespace FluentCMS.Web.UI.Plugins.UserManagement;

public partial class UserCreatePlugin
{
    public const string FORM_NAME = "UserCreateForm";

    [SupplyParameterFromForm(FormName = FORM_NAME)]
    private UserCreateRequest? Model { get; set; }

    private List<RoleDetailResponse>? Roles { get; set; }

    protected override async Task OnParametersSetAsync()
    {
        if (Roles is null)
        {
            var rolesResponse = await GetApiClient<RoleClient>().GetAllAsync();
            Roles = rolesResponse?.Data?.ToList() ?? [];
        }
        Model ??= new();
    }

    private async Task OnSubmit()
    {
        Model!.RoleIds ??= [];
        await GetApiClient<UserClient>().CreateAsync(Model);
        NavigateBack();
    }
}
