namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public partial class ContentTypeFieldCreate
{
    private FieldType? SelectedFieldType { get; set; }

    private FieldTypes FieldTypes { get; set; } = [];

    private async Task OnBackToTypeSelector()
    {
        SelectedFieldType = default!;
        await Task.CompletedTask;
    }

    private async Task OnTypeSelect(FieldType type)
    {
        ContentTypeField!.Type = type.Key;
        SelectedFieldType = type;
        await Task.CompletedTask;
    }

    private async Task OnFieldCreate(ContentTypeField contentTypeField)
    {
        await GetApiClient().SetFieldAsync(ContentType!.Id, contentTypeField);
        await OnComplete.InvokeAsync();
    }
}
