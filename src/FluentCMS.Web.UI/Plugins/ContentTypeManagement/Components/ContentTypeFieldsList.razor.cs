namespace FluentCMS.Web.UI.Plugins.ContentTypeManagement;

public partial class ContentTypeFieldsList
{
    [Parameter]
    public ContentTypeDetailResponse? ContentType { get; set; }

    [Parameter]
    public EventCallback<ContentTypeField> OnEdit { get; set; }

    [Parameter]
    public EventCallback<ContentTypeField> OnDelete { get; set; }

    private FieldTypes FieldTypes { get; set; } = [];

    private ContentTypeField? SelectedField { get; set; }

    private async Task OnEditClick(ContentTypeField contentTypeField)
    {
        await OnEdit.InvokeAsync(contentTypeField);
    }

    private async Task OnDeleteClick(ContentTypeField contentTypeField)
    {
        SelectedField = contentTypeField;
        await Task.CompletedTask;
    }
    
    private async Task OnDeleteConfirm()
    {
        await OnDelete.InvokeAsync(SelectedField);
        SelectedField = default!;
    }
     
    private async Task OnCancel()
    {
        SelectedField = default!;
        await Task.CompletedTask;
    }
    
}
