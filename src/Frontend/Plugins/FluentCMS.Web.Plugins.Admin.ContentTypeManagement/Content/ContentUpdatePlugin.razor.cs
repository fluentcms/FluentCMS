namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement.Content;

public partial class ContentUpdatePlugin
{
    public const string FORM_NAME = "ContentUpdateForm";

    [SupplyParameterFromQuery(Name = "id")]
    public Guid Id { get; set; }

    private ContentDetailResponse View { get; set; }

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

            var contentResponse = await GetApiClient<ContentClient>().GetAllAsync(ContentTypeSlug!);
            View = contentResponse?.Data.FirstOrDefault(x => x.Id == Id);

            if (View != null)
            {
                foreach (var key in View.Value.Keys)
                {
                    FieldValues.Add((IFieldValue)new FieldValue<object>() { Name = key, Value = View.Value[key] });
                }
            }
        }
    }

    private static Type GetFormFieldType(IFieldModel fieldModel)
    {
        return FieldTypes.All[fieldModel.Type].FormComponents.Where(x => x.Name == fieldModel.FormViewComponent).FirstOrDefault()?.Type ??
            throw new NotSupportedException($"Field type '{fieldModel.Type}' is not supported.");
    }

    private Dictionary<string, object> GetFormFieldParameters(IFieldModel fieldModel)
    {
        var fieldValue = (IFieldValue)FieldValues.FirstOrDefault(x => x.Name == fieldModel.Name);

        var parameters = new Dictionary<string, object>
        {
            { "Field", fieldModel },
            { "FieldValue", (IFieldValue)fieldValue }
        };

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
