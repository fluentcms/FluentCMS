namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public partial class ContentTypeFieldCreate
{
    private FieldType? FieldType { get; set; }

    private FieldTypes FieldTypes { get; set; } = [];

    private async Task OnBackToTypeSelector()
    {
        FieldType = default!;
        await Task.CompletedTask;
    }

    private async Task OnTypeSelect(FieldType type)
    {
        ContentTypeField!.Type = type.Key;
        FieldType = type;
        await Task.CompletedTask;
    }

    private async Task OnFieldCreate(ContentTypeField contentTypeField)
    {
        FieldType = default!;
        await GetApiClient().SetFieldAsync(ContentType!.Id, contentTypeField);
        await OnComplete.InvokeAsync();
    }
}
