namespace FluentCMS.Web.UI.Plugins.UserManagement;

public partial class UserUpdatePlugin
{
    public const string FORM_NAME = "UserUpdateForm";

    [SupplyParameterFromQuery(Name = "id")]
    private Guid Id { get; set; }

    [SupplyParameterFromForm(FormName = FORM_NAME)]
    private UserUpdateRequest? Model { get; set; }

    private List<RoleDetailResponse>? Roles { get; set; }

    protected override async Task OnInitializedAsync()
    {
        if (Roles is null)
        {
            var rolesResponse = await GetApiClient<RoleClient>().GetAllAsync();
            Roles = rolesResponse?.Data?.ToList() ?? [];
        }
        if (Model is null)
        {
            var userResponse = await GetApiClient<UserClient>().GetAsync(Id);
            var user = userResponse.Data;
            Model = Mapper.Map<UserUpdateRequest>(user);
            Model.RoleIds = user.Roles?.Select(r => r.Id).ToList() ?? [];
        }
    }

    private async Task OnSubmit()
    {
        Model!.RoleIds ??= [];
        await GetApiClient<UserClient>().UpdateAsync(Model);
        NavigateBack();
    }
}
