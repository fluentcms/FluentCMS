namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement.Content;

public partial class ContentCreatePlugin
{
    public const string FORM_NAME = "ContentCreateForm";

    [SupplyParameterFromForm(FormName = FORM_NAME)]
    private Dictionary<string, object?> Model { get; set; } = [];

    private List<IFieldModel> Fields { get; set; } = [];
    private List<IFieldValue> FieldValues { get; set; } = [];

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        if (ContentType != null)
        {
            Fields = ContentType?.Fields?.Select(x => x.ToFieldModel()).OrderBy(x => x.FormViewOrder).ToList() ?? [];
        }
    }
    private static Type GetFormFieldType(IFieldModel fieldModel)
    {
        return FieldTypes.All[fieldModel.Type].FormComponents.Where(x => string.IsNullOrEmpty(fieldModel.FormViewComponent) || x.Name == fieldModel.FormViewComponent).FirstOrDefault()?.Type ??
            throw new NotSupportedException($"Field type '{fieldModel.Type}' is not supported.");
    }

    private Dictionary<string, object> GetFormFieldParameters(IFieldModel fieldModel)
    {
        // check type of fieldModel and return parameters
        var parameters = new Dictionary<string, object>
        {
            { "Field", fieldModel },
            { "FieldValue", fieldModel.GetFieldValue() },
            { nameof(ContentTypeField), ContentType!.Fields!.Where(x=> x.Name==fieldModel.Name).Single() }
        };

        FieldValues.Add((IFieldValue)parameters["FieldValue"]);

        return parameters;
    }

    private async Task OnSubmit()
    {
        await ApiClient.Content.CreateAsync(ViewState.Site.Id, ContentTypeSlug!, FieldValues.ToDictionary(x => x.Name, x => x.GetValue() ?? default!));

        NavigateBack();
    }
}
