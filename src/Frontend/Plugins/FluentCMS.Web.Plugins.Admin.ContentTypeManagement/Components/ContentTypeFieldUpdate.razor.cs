namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public partial class ContentTypeFieldUpdate
{
    private FieldType? FieldType { get; set; }

    protected override void OnParametersSet()
    {
        if (!string.IsNullOrEmpty(ContentTypeField?.Type))
        {
            var fieldTypes = new FieldTypes();
            FieldType = fieldTypes[ContentTypeField.Type];
        }
        base.OnParametersSet();
    }
    private async Task OnFieldUpdate(ContentTypeField contentTypeField)
    {
        await GetApiClient().SetFieldAsync(ContentType!.Id, contentTypeField);
        await OnComplete.InvokeAsync();
    }
}
