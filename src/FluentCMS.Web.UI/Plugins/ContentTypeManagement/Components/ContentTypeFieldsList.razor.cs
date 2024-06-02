namespace FluentCMS.Web.UI.Plugins.ContentTypeManagement;

public partial class ContentTypeFieldsList
{
    [Parameter]
    public ContentTypeDetailResponse? ContentType { get; set; }

    [Parameter]
    public EventCallback<ContentTypeField> OnEdit { get; set; }

    [Parameter]
    public EventCallback<ContentTypeField> OnDelete { get; set; }

    private async Task OnEditClick(ContentTypeField contentTypeField)
    {
        await OnEdit.InvokeAsync(contentTypeField);
    }

    private async Task OnDeleteClick(ContentTypeField contentTypeField)
    {
        await OnDelete.InvokeAsync(contentTypeField);
    }
}
