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

    private List<ContentTypeField> GetVisibleFields()
    {
        if (ContentType?.Fields == null)
            return [];

        var result = new List<ContentTypeField>();

        var visibleKey = nameof(IFieldModel.DataTableVisible);

        foreach (var field in ContentType.Fields.Where(x => x.GetBoolean(visibleKey) == true))
            result.Add(field);

        return [.. result.OrderBy(x => x.GetDecimal(nameof(IFieldModel.DataTableColumnOrder)))];
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
