namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public partial class ContentTypeFieldCreate
{
    private IFieldModel Field { get; set; } = default!;

    private async Task OnBackToTypeSelector()
    {
        await Task.CompletedTask;
    }

    private async Task OnTypeSelect(string type)
    {
        ContentTypeField!.Type = type;
        await Task.CompletedTask;
    }

    private async Task OnFieldCreate(ContentTypeField contentTypeField)
    {
        await GetApiClient().SetFieldAsync(ContentType!.Id, contentTypeField);
        await OnComplete.InvokeAsync();
    }

    private Dictionary<string, object> GetParameters()
    {
        var parameters = new Dictionary<string, object>
        {
            { nameof(ContentTypeField), ContentTypeField! },
            { "OnSubmit", OnFieldCreate },
            { "OnCancel", OnComplete }
        };

        return parameters;
    }
}
