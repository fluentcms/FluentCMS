namespace FluentCMS.Web.UI.Plugins;

public partial class LayoutCreatePlugin
{
    public const string FORM_NAME = "LayoutCreateForm";

    [SupplyParameterFromForm(FormName = FORM_NAME)]
    private LayoutCreateRequest Model { get; set; } = new();

    private async Task OnSubmit()
    {
        await GetApiClient<LayoutClient>().CreateAsync(Model);
        NavigateBack();
    }
}
