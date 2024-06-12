namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement.Content;

public partial class ContentCreatePlugin
{
    public const string FORM_NAME = "ContentCreateForm";

    [SupplyParameterFromForm(FormName = FORM_NAME)]
    private ContentCreateRequest Model { get; set; } = new();

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

            // TODO: Initialize FieldValues based on `Model` value
        }
    }
    private static Type GetFormFieldType(IFieldModel fieldModel)
    {
        Console.WriteLine(fieldModel.FormViewComponent);
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
