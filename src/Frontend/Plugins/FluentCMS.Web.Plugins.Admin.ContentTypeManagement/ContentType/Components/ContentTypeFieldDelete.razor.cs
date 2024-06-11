namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public partial class ContentTypeFieldDelete
{
    private async Task OnConfirm()
    {
        await GetApiClient().DeleteFieldAsync(ContentType!.Id, ContentTypeField!.Name!);
        await OnComplete.InvokeAsync();
    }
}
