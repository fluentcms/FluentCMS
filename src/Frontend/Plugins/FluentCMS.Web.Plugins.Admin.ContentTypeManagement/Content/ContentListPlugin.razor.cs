namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement.Content;

public partial class ContentListPlugin
{
    protected override async Task OnInitializedAsync()
    {
        if (!string.IsNullOrEmpty(ContentTypeSlug))
        {
            var contentTypeResponse = await GetApiClient<ContentTypeClient>().GetBySlugAsync(ContentTypeSlug);
            ContentType = contentTypeResponse?.Data;
            await Load();
        }
    }

    private async Task Load()
    {
        if (!string.IsNullOrEmpty(ContentTypeSlug) && ContentType != null)
        {
            var contentsResponse = await GetApiClient<ContentClient>().GetAllAsync(ContentTypeSlug);
            Contents = contentsResponse?.Data?.ToList() ?? [];
        }
    }

    #region Delete Content

    private Guid? SelectedContentId { get; set; }

    private async Task OnDelete()
    {
        if (SelectedContentId == null)
            return;

        await GetApiClient<ContentClient>().DeleteAsync(ContentTypeSlug!, SelectedContentId.Value);
        await Load();
        SelectedContentId = default;
    }

    private async Task OnConfirm(Guid contentId)
    {
        SelectedContentId = contentId;
        await Task.CompletedTask;
    }
    private async Task OnConfirmClose()
    {
        SelectedContentId = default;
        await Task.CompletedTask;
    }

    #endregion
}
