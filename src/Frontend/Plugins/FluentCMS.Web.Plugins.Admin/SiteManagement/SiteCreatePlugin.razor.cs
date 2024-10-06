namespace FluentCMS.Web.Plugins.Admin.SiteManagement;

public partial class SiteCreatePlugin
{
    public const string FORM_NAME = "SiteCreateForm";

    [SupplyParameterFromForm(FormName = FORM_NAME)]
    private SiteCreateModel? Model { get; set; }
    private List<string>? Templates { get; set; }

    protected override async Task OnInitializedAsync()
    {
        if (Templates is null)
        {
            var templatesResponse = await ApiClient.Setup.GetTemplatesAsync();
            Templates = templatesResponse?.Data?.ToList() ?? [];
        }

        Model ??= new();
    }

    private async Task OnSubmit()
    {
        if (Model != null)
        {
            var siteCreateRequest = Model.ToRequest();
            var response = await ApiClient.Site.CreateAsync(siteCreateRequest);
            await ApiClient.Settings.UpdateAsync(Model.ToSettings(response.Data.Id));
        }
        NavigateBack();
    }
}
