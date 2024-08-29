namespace FluentCMS.Web.Plugins.Admin.GlobalSettingManagement;

public partial class GlobalSettingsPlugin
{
    public const string FORM_NAME = "GlobalSettingForm";

    [SupplyParameterFromForm(FormName = FORM_NAME)]
    private GlobalSettingsUpdateRequest? Model { get; set; }

    private string SuperAdminUserNames { get; set; } = string.Empty;

    protected override async Task OnInitializedAsync()
    {
        if (string.IsNullOrEmpty(SuperAdminUserNames))
        {
            var globalSettings = await ApiClient.GlobalSettings.GetAsync();
            SuperAdminUserNames = string.Join(",", globalSettings?.Data?.SuperAdmins);
        }

        if (Model is null)
            Model = new();
    }

    private async Task OnSubmit()
    {
        Model!.SuperAdmins = SuperAdminUserNames.Split(",");

        await ApiClient.GlobalSettings.UpdateAsync(Model);
        NavigateTo("/admin");
    }
}
