namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public partial class StringFieldFormRichText
{
    [CascadingParameter]
    private ContentTypeField? ContentTypeField { get; set; }

    [Parameter]
    public string? Value { get; set; } = default!;

    private StringFieldModel? FieldModel { get; set; }

    protected override Task OnInitializedAsync()
    {
        if (ContentTypeField is not null)
            FieldModel = ContentTypeField.ToFieldModel<StringFieldModel>();

        if (!string.IsNullOrEmpty(FieldModel.DefaultValue))
            Value = FieldModel.DefaultValue;

        return base.OnInitializedAsync();
    }
}
