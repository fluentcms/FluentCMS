namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public partial class ContentTypeFieldDelete
{
    [Parameter]
    public ContentTypeField? Model { get; set; }

    private async Task OnConfirm()
    {
        await GetApiClient().DeleteFieldAsync(ContentType!.Id, Model!.Name!);
        await OnComplete.InvokeAsync();
    }
}
