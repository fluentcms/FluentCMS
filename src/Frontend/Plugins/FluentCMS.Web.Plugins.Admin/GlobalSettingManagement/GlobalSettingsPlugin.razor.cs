namespace FluentCMS.Web.Plugins.Admin.GlobalSettingManagement;

public partial class GlobalSettingsPlugin
{
    public const string FORM_NAME = "GlobalSettingsForm";

    [SupplyParameterFromForm(FormName = FORM_NAME)]
    private GlobalSettingsUpdateModel? Model { get; set; }

    protected override async Task OnInitializedAsync()
    {
        if (Model is null)
        {
            var globalSettings = await ApiClient.GlobalSettings.GetAsync();
            Model = new()
            {
                SuperAdminUserNames = string.Join(",", globalSettings?.Data?.SuperAdmins ?? [])
            };
        }
    }

    private async Task OnSubmit()
    {
        var superAdmins = Model?.SuperAdminUserNames.Split(",", StringSplitOptions.TrimEntries).Select(x => x.Trim()).ToList() ?? [];

        var globalSettingsUpdateRequest = new GlobalSettingsUpdateRequest
        {
            SuperAdmins = superAdmins
        };
        await ApiClient.GlobalSettings.UpdateAsync(globalSettingsUpdateRequest);
        NavigateTo("/admin");
    }
}
