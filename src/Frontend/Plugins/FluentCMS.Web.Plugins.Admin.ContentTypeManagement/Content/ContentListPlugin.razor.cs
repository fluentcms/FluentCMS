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
        // check type of fieldModel and return parameters
        var parameters = new Dictionary<string, object>
        {
            { "Field", fieldModel }
        };

        switch (fieldModel.Type)
        {
            case FieldTypes.STRING:
                parameters.Add("FieldValue", new FieldValue<string?> { Name = fieldModel.Name, Value = (string?)content.Value[fieldModel.Name] });
                break;
            case FieldTypes.NUMBER:
                parameters.Add("FieldValue", new FieldValue<decimal?> { Name = fieldModel.Name, Value = (decimal?)content.Value[fieldModel.Name] });
                break;
            case FieldTypes.BOOLEAN:
                parameters.Add("FieldValue", new FieldValue<bool> { Name = fieldModel.Name, Value = (bool)content.Value[fieldModel.Name] });
                break;
            case FieldTypes.DATE_TIME:
                parameters.Add("FieldValue", new FieldValue<DateTime?> { Name = fieldModel.Name, Value = (DateTime?)content.Value[fieldModel.Name] });
                break;
            default:
                throw new NotSupportedException($"Field type '{fieldModel.Type}' is not supported.");
        }

        return parameters;
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
