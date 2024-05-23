namespace FluentCMS.Web.UI.Plugins;

public partial class SiteCreate
{
    public const string FORM_NAME = "SiteCreateForm";

    [SupplyParameterFromForm(FormName = FORM_NAME)]
    private SiteCreateRequest Model { get; set; } = new();

    private async Task OnSubmit()
    {
        await GetApiClient<SiteClient>().CreateAsync(Model);
        NavigateBack();
    }
}
