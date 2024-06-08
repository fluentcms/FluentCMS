using System.Reflection;

namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement.Content;

public partial class ContentCreatePlugin
{
    public const string FORM_NAME = "ContentCreateForm";

    [SupplyParameterFromForm(FormName = FORM_NAME)]
    private ContentCreateRequest Model { get; set; } = new();

    protected override async Task OnInitializedAsync()
    {
        Model ??= new ContentCreateRequest();
        if (ContentType is null && !string.IsNullOrEmpty(ContentTypeSlug))
        {
            var contentTypeResponse = await GetApiClient<ContentTypeClient>().GetBySlugAsync(ContentTypeSlug!);
            ContentType = contentTypeResponse.Data;
        }
    }

    private async Task OnSubmit()
    {
        await GetApiClient<ContentClient>().CreateAsync(ContentTypeSlug!, Model);
        NavigateBack();
    }

    private List<ContentTypeField> GetFields()
    {
        if (ContentType?.Fields == null)
            return [];

        return [.. ContentType.Fields.OrderBy(x => x.GetDecimal(nameof(IFieldModel.FormViewOrder)))];
    }

    private Type GetFormFieldViewType(ContentTypeField contentTypeField)
    {
        var typeName = contentTypeField.GetString(nameof(IFieldModel.FormViewComponent));

        // find view type by name in this assembly
        var viewType = Assembly.GetExecutingAssembly().GetTypes().FirstOrDefault(x => x.Name == typeName);

        return viewType ?? typeof(StringFieldFormText);
    }

    private static IDictionary<string, object> GetParameters(ContentTypeField field)
    {
        return new Dictionary<string, object>();
        if (field == null)
            return new Dictionary<string, object>();

        return new Dictionary<string, object>
        {
            { "ContentTypeField", field }
        };
    }

}
