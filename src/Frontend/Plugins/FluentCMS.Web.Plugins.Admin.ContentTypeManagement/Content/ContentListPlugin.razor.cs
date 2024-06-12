namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement.Content;

public partial class ContentListPlugin
{
    private List<ContentDetailResponse>? Contents { get; set; }

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        await Load();
    }

    private async Task Load()
    {
        if (!string.IsNullOrEmpty(ContentTypeSlug) && ContentType != null)
        {
            var contentsResponse = await GetApiClient<ContentClient>().GetAllAsync(ContentTypeSlug);
            Contents = contentsResponse?.Data?.ToList() ?? [];
        }
    }

    private List<IFieldModel> GetVisibleFields()
    {
        if (ContentType?.Fields == null)
            return [];

        var fields = ContentType.Fields.Select(x => x.ToFieldModel()).Where(x => x.DataTableVisible);

        return [.. fields.OrderBy(x => x.DataTableColumnOrder)];
    }

    private static Type GetDataTableComponent(string fieldTypeName, string viewName)
    {
        return FieldTypes.All[fieldTypeName].DataTableComponents.Where(x => x.Name == viewName).FirstOrDefault()?.Type ??
            throw new NotSupportedException($"Field type '{fieldTypeName}' is not supported.");
    }

    private static Dictionary<string, object> GetParameters(ContentDetailResponse content, IFieldModel fieldModel)
    {
        return new Dictionary<string, object>
        {
            { "Field", fieldModel },
            { "FieldValue", fieldModel.GetFieldValue(content?.Value)}
        };
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
