namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public partial class ContentTypeUpdatePlugin
{
    public const string FORM_NAME = "ContentTypeUpdateForm";

    [SupplyParameterFromQuery(Name = "id")]
    private Guid Id { get; set; }

    [SupplyParameterFromForm(FormName = FORM_NAME)]
    private ContentTypeUpdateRequest? Model { get; set; }

    protected override async Task OnInitializedAsync()
    {
        if (Model is null)
        {
            var contentTypeResponse = await ApiClient.ContentType.GetByIdAsync(Id);
            Model = Mapper.Map<ContentTypeUpdateRequest>(contentTypeResponse.Data);
        }
    }

    private async Task OnSubmit()
    {
        await ApiClient.ContentType.UpdateAsync(Model);
        NavigateBack();
    }
}
