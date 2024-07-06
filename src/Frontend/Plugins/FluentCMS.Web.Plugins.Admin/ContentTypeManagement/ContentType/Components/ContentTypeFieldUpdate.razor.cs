namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public partial class ContentTypeFieldUpdate
{
    private async Task OnFieldUpdate(ContentTypeField contentTypeField)
    {
        await ApiClient.ContentType.SetFieldAsync(ContentType!.Id, contentTypeField);
        await OnComplete.InvokeAsync();
    }

    private Dictionary<string, object> GetParameters()
    {
        var parameters = new Dictionary<string, object>
        {
            { nameof(ContentTypeField), ContentTypeField! },
            { "Model", ContentTypeField.ToFieldModel() },
            { "OnCancel", OnComplete },
            { "OnSubmit", EventCallback.Factory.Create<ContentTypeField>(this, OnFieldUpdate) }
        };

        return parameters;
    }
}
