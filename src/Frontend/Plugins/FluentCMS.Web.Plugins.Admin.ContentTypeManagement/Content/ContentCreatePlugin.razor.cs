namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement.Content;

public partial class ContentCreatePlugin
{
    public const string FORM_NAME = "ContentCreateForm";

    [SupplyParameterFromForm(FormName = FORM_NAME)]
    private ContentCreateRequest? Model { get; set; }

    private Dictionary<string, string?> StringValueDict { get; set; } = [];
    private Dictionary<string, decimal?> DecimalValueDict { get; set; } = [];
    private Dictionary<string, bool> BooleanValueDict { get; set; } = [];
    private Dictionary<string, DateTime?> DateValueDict { get; set; } = [];

    protected override async Task OnInitializedAsync()
    {
        if (ContentType is null && !string.IsNullOrEmpty(ContentTypeSlug))
        {
            var contentTypeResponse = await GetApiClient<ContentTypeClient>().GetBySlugAsync(ContentTypeSlug!);
            ContentType = contentTypeResponse.Data;

            if (ContentType is null)
                return;

            Model = new ContentCreateRequest() { Value = new Dictionary<string, object?>() };

            foreach (var field in ContentType.Fields ?? [])
            {
                switch (field.Type)
                {
                    case FieldTypes.STRING:
                        StringValueDict[field.Name!] = null;
                        break;

                    case FieldTypes.NUMBER:
                        DecimalValueDict[field.Name!] = null;
                        break;

                    case FieldTypes.BOOLEAN:
                        BooleanValueDict[field.Name!] = false;
                        break;

                    case FieldTypes.DATE:
                        DateValueDict[field.Name!] = null;
                        break;

                    default:
                        break;
                }
            }
        }
    }

    private async Task OnSubmit()
    {
        Model ??= new ContentCreateRequest();
        Model.Value = new Dictionary<string, object?>();
        // add string values
        foreach (var (key, value) in StringValueDict)
        {
            Model.Value[key] = value;
        }
        await GetApiClient<ContentClient>().CreateAsync(ContentTypeSlug!, Model);
        NavigateBack();
    }

    private List<ContentTypeField> GetFields()
    {
        if (ContentType?.Fields == null)
            return [];

        return [.. ContentType.Fields.OrderBy(x => x.GetDecimal(nameof(IFieldModel.FormViewOrder)))];
    }

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
