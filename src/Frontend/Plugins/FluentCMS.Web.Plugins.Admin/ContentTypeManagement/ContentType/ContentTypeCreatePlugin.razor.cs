namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public partial class ContentTypeCreatePlugin
{
    public const string FORM_NAME = "ContentTypeCreateForm";

    [SupplyParameterFromForm(FormName = FORM_NAME)]
    private ContentTypeCreateRequest Model { get; set; } = new();

    private async Task OnSubmit()
    {
        Model.SiteId = ViewState.Site.Id;
        await ApiClient.ContentType.CreateAsync(Model);
        NavigateBack();
    }
}
