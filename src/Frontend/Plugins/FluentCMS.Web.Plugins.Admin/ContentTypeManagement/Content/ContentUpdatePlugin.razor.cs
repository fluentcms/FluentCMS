namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement.Content;

public partial class ContentUpdatePlugin
{
    public const string FORM_NAME = "ContentUpdateForm";

    [Inject]
    protected ApiClientFactory ApiClient { get; set; } = default!;

    [SupplyParameterFromQuery(Name = "id")]
    public Guid Id { get; set; }

    private ContentDetailResponse? Model { get; set; }
    private List<IFieldModel> Fields { get; set; } = [];
    private List<IFieldValue> FieldValues { get; set; } = [];

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();

        if (ContentType != null)
        {
            var contentTypeResponse = await ApiClient.ContentType.GetBySlugAsync(ContentTypeSlug!);
            ContentType = contentTypeResponse.Data;
            Fields = ContentType?.Fields?.Select(x => x.ToFieldModel()).OrderBy(x => x.FormViewOrder).ToList() ?? [];

            var contentResponse = await ApiClient.Content.GetByIdAsync(ContentTypeSlug!, Id);
            Model = contentResponse?.Data;

            if (Fields != null)
            {
                foreach (var field in Fields)
                {
                    FieldValues.Add(field.GetFieldValue(Model?.Data ?? []));
                }
            }
        }
    }

    private static Type GetFormFieldType(IFieldModel fieldModel)
    {
        return FieldTypes.All[fieldModel.Type].FormComponents.Where(x => x.Name == fieldModel.FormViewComponent).FirstOrDefault()?.Type ??
            throw new NotSupportedException($"Field type '{fieldModel.FormViewComponent}' is not supported.");
    }

    private Dictionary<string, object?> GetFormFieldParameters(IFieldModel fieldModel)
    {
        var fieldValue = FieldValues.First(x => x.Name == fieldModel.Name);

        var parameters = new Dictionary<string, object?>
        {
            { "Field", fieldModel },
            { "FieldValue", fieldValue },
            { nameof(ContentTypeField), ContentType.Fields.Where(x=> x.Name==fieldModel.Name).SingleOrDefault() }
        };

        return parameters;
    }

    private async Task OnSubmit()
    {
        var data = FieldValues.ToDictionary(x => x.Name, x => x.GetValue());
        await ApiClient.Content.UpdateAsync(ContentTypeSlug!, Id, data);

        NavigateBack();
    }
}
