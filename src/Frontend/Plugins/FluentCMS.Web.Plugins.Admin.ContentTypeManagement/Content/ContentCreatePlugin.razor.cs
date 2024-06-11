namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement.Content;

public partial class ContentCreatePlugin
{
    private List<IFieldModel> Fields { get; set; } = [];
    private List<IFieldValue> FieldValues { get; set; } = [];

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        if (ContentType != null)
        {
            var contentTypeResponse = await GetApiClient<ContentTypeClient>().GetBySlugAsync(ContentTypeSlug!);
            ContentType = contentTypeResponse.Data;
            Fields = ContentType?.Fields?.Select(x => x.ToFieldModel()).OrderBy(x => x.FormViewOrder).ToList() ?? [];
        }
    }
    private static Type GetFormFieldType(IFieldModel fieldModel)
    {
        return FieldTypes.All[fieldModel.Type].FormComponents.Where(x => x.Name == fieldModel.FormViewComponent).FirstOrDefault()?.Type ??
            throw new NotSupportedException($"Field type '{fieldModel.Type}' is not supported.");
    }

    private Dictionary<string, object> GetFormFieldParameters(IFieldModel fieldModel)
    {
        // check type of fieldModel and return parameters
        var parameters = new Dictionary<string, object>
        {
            { "Field", fieldModel },
            { "FieldValue", fieldModel.GetFieldValue() }
        };

        FieldValues.Add((IFieldValue)parameters["FieldValue"]);

        return parameters;
    }

    private async Task OnSubmit()
    {
        var request = new ContentCreateRequest
        {
            Value = FieldValues.ToDictionary(x => x.Name, x => x.GetValue())
        };

        await GetApiClient<ContentClient>().CreateAsync(ContentTypeSlug!, request);

        NavigateBack();
    }
}
