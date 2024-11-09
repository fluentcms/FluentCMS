namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public partial class ContentTypeListPlugin
{
    private List<ContentTypeDetailResponse> ContentTypes { get; set; } = [];

    protected override async Task OnInitializedAsync()
    {
        await Load();
    }

    private async Task Load()
    {
        var contentTypesResponse = await ApiClient.ContentType.GetAllAsync(ViewState.Site.Id);
        ContentTypes = contentTypesResponse?.Data?.ToList() ?? [];
    }

    #region Delete Content Type

    private Guid? SelectedContentTypeId { get; set; }

    private async Task OnDelete()
    {
        if (SelectedContentTypeId == null)
            return;

        await ApiClient.ContentType.DeleteAsync(SelectedContentTypeId.Value);
        await Load();
        SelectedContentTypeId = default;
    }

    private async Task OnConfirm(Guid contentTypeId)
    {
        SelectedContentTypeId = contentTypeId;
        await Task.CompletedTask;
    }
    private async Task OnConfirmClose()
    {
        SelectedContentTypeId = default;
        await Task.CompletedTask;
    }

    #endregion

}
