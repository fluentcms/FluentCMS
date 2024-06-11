namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement.Content;

public partial class ContentCreatePlugin
{
    //private List<IFieldModel> Fields { get; set; } = [];

    //protected override async Task OnInitializedAsync()
    //{
    //    if (ContentType is null && !string.IsNullOrEmpty(ContentTypeSlug))
    //    {
    //        var contentTypeResponse = await GetApiClient<ContentTypeClient>().GetBySlugAsync(ContentTypeSlug!);
    //        ContentType = contentTypeResponse.Data;

    //        if (ContentType is null)
    //            return;

    //        foreach (var contentTypeField in ContentType.Fields ?? [])
    //        {
    //            if (contentTypeField.Type == FieldTypes.STRING)
    //            {
    //                var newField = contentTypeField.ToFieldModel<StringFieldModel>();
    //                //newField.Value = newField.DefaultValue;
    //                Fields.Add(newField);
    //            }
    //        }
    //    }
    //}

    //private async Task OnSubmit()
    //{
    //    //var request = new ContentCreateRequest
    //    //{
    //    //    Value = Fields.ToDictionary(x => x.Name, x => x.DataTableViewComponent)
    //    //};

    //    //await GetApiClient<ContentClient>().CreateAsync(ContentTypeSlug!, request);

    //    NavigateBack();
    //}

    //private List<ContentTypeField> GetFields()
    //{
    //    if (ContentType?.Fields == null)
    //        return [];

    //    return [.. ContentType.Fields.OrderBy(x => x.GetDecimal(nameof(IFieldModel.FormViewOrder)))];
    //}

    //private Type GetFormFieldViewType(ContentTypeField contentTypeField)
    //{
    //    var typeName = contentTypeField.GetString(nameof(IFieldModel.FormViewComponent));

    //    // find view type by name in this assembly
    //    var viewType = Assembly.GetExecutingAssembly().GetTypes().FirstOrDefault(x => x.Name == typeName);

    //    return viewType ?? typeof(StringFieldFormText);
    //}

    //private static IDictionary<string, object> GetParameters(ContentTypeField field)
    //{
    //    return new Dictionary<string, object>();
    //    if (field == null)
    //        return new Dictionary<string, object>();

    //    return new Dictionary<string, object>
    //    {
    //        { "ContentTypeField", field }
    //    };
    //}

}
