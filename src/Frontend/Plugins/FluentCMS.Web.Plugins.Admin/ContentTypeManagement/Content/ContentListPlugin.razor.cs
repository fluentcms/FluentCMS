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
        if (!string.IsNullOrEmpty(ContentTypeSlug))
        {
            var contentsResponse = await ApiClient.Content.GetAllAsync(ContentTypeSlug);
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
        return FieldTypes.All[fieldTypeName].DataTableComponents.FirstOrDefault(x => x.Name == viewName)?.Type ??
               throw new NotSupportedException($"Field type '{fieldTypeName}' is not supported.");
    }

    private Dictionary<string, object> GetParameters(ContentDetailResponse content, IFieldModel fieldModel)
    {
        return new Dictionary<string, object>
        {
            { "Field", fieldModel },
            { "FieldValue", fieldModel.GetFieldValue(content?.Data)},
            { nameof(ContentTypeField), ContentType.Fields.Where(x => x.Name == fieldModel.Name).SingleOrDefault() }
        };
    }


    #region Delete Content

    private Guid? SelectedContentId { get; set; }

    private async Task OnDelete()
    {
        if (SelectedContentId == null)
            return;

        await ApiClient.Content.DeleteAsync(ContentTypeSlug!, SelectedContentId.Value);
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
