namespace FluentCMS.Web.Plugins.Admin.SiteManagement;

public partial class SiteCreatePlugin
{
    public const string FORM_NAME = "SiteCreateForm";

    [SupplyParameterFromForm(FormName = FORM_NAME)]
    private SiteCreateRequest? Model { get; set; }
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
        await ApiClient.Site.CreateAsync(Model);
        NavigateBack();
    }
}
