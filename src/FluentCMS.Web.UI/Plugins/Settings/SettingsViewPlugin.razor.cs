namespace FluentCMS.Web.UI.Plugins.Settings;

public partial class SettingsViewPlugin
{

    public const string FORM_NAME = "UserCreateForm";

    [SupplyParameterFromForm(FormName = FORM_NAME)]
    private GlobalSettingsUpdateRequest? Model { get; set; }

    private List<UserDetailResponse>? Users { get; set; }

    protected override async Task OnParametersSetAsync()
    {
        if (Users is null)
        {
            var usersResponse = await GetApiClient<UserClient>().GetAllAsync();
            Users = usersResponse?.Data?.ToList() ?? [];
        }
        Model ??= new();
    }

    private async Task OnSubmit()
    {
        await GetApiClient<GlobalSettingsClient>().UpdateAsync(Model);
        NavigateBack();
    }
}


