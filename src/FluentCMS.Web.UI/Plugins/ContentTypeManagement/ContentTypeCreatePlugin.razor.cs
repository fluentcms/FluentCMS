namespace FluentCMS.Web.UI.Plugins.ContentTypeManagement;

public partial class ContentTypeCreatePlugin
{
    public const string FORM_NAME = "ContentTypeCreateForm";

    [SupplyParameterFromForm(FormName = FORM_NAME)]
    private ContentTypeCreateRequest Model { get; set; } = new();

    private async Task OnSubmit()
    {
        await GetApiClient<ContentTypeClient>().CreateAsync(Model);
        NavigateBack();
    }
}
