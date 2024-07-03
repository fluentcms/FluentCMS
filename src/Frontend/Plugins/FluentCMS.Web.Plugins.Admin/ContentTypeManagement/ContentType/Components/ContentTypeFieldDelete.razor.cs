namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public partial class ContentTypeFieldDelete
{
    private async Task OnConfirm()
    {
        await ApiClient.ContentType.DeleteFieldAsync(ContentType!.Id, ContentTypeField!.Name!);
        await OnComplete.InvokeAsync();
    }
}
