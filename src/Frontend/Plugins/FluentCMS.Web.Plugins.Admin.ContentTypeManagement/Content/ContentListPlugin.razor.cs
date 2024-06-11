using System.Reflection;

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

        var fields = new List<IFieldModel>();

        //foreach (var contentTypeField in ContentType.Fields)
        //{
        //    IFieldModel field;

        //    // check the content type field type and return the parameters
        //    switch (contentTypeField.Type)
        //    {
        //        case FieldTypes.BOOLEAN:
        //            field = contentTypeField.ToFieldModel<BooleanFieldModel>();
        //            break;
        //        case FieldTypes.NUMBER:
        //            field = contentTypeField.ToFieldModel<NumberFieldModel>();
        //            break;
        //        case FieldTypes.DATE:
        //            field = contentTypeField.ToFieldModel<DateFieldModel>();
        //            break;
        //        default:
        //            field = contentTypeField.ToFieldModel<StringFieldModel>();
        //            break;
        //    }

        //    fields.Add(field);
        //}

        return [.. fields.OrderBy(x => x.DataTableColumnOrder)];
    }

    //private static Type GetDataTableFieldViewType(IFieldModel field)
    //{
    //    // find view type by name in this assembly
    //    var viewType = Assembly.GetExecutingAssembly().GetTypes().FirstOrDefault(x => x.Name == field.DataTableViewComponent);

    //    return viewType ?? typeof(StringFieldDataTableView);
    //}

    //private static Dictionary<string, object> GetParameters(ContentDetailResponse content, IFieldModel field)
    //{
    //    // get the value of the field from the content
    //    var value = content.Value[field.Name];
    //    if (value != null)
    //    {
    //        switch (field.Type)
    //        {
    //            case FieldTypes.BOOLEAN:
    //                // the field type is BooleanFieldModel and we should set the Value and DefaultValue property
    //                //((BooleanFieldModel)field).Value = (bool)value;
    //                if (content.Value.ContainsKey("DefaultValue"))
    //                    ((BooleanFieldModel)field).DefaultValue = (bool)(content.Value["DefaultValue"]);
    //                break;

    //            case FieldTypes.NUMBER:
    //                // the field type is NumberFieldModel and we should set the Value and DefaultValue property
    //                //((NumberFieldModel)field).Value = (decimal)value;
    //                if (content.Value.ContainsKey("DefaultValue"))
    //                    ((NumberFieldModel)field).DefaultValue = (decimal)(content.Value["DefaultValue"]);
    //                break;

    //            case FieldTypes.DATE:
    //                // the field type is DateFieldModel and we should set the Value and DefaultValue property
    //                //((DateFieldModel)field).Value = (DateTime)value;
    //                if (content.Value.ContainsKey("DefaultValue"))
    //                    ((DateFieldModel)field).DefaultValue = (DateTime)(content.Value["DefaultValue"]);
    //                break;

    //            case FieldTypes.STRING:
    //                // the field type is StringFieldModel and we should set the Value and DefaultValue property
    //                //((StringFieldModel)field).Value = (string)value;
    //                if (content.Value.ContainsKey("DefaultValue"))
    //                    ((StringFieldModel)field).DefaultValue = (string)(content.Value["DefaultValue"]);
    //                break;

    //            default:
    //                throw new Exception($"Field type {field.Type} in field {field.Name} not supported");
    //        }
    //    }

    //    return new Dictionary<string, object>
    //    {
    //        { "Model", field }
    //    };
    //}

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
