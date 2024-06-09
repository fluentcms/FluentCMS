namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public partial class BooleanFieldFormSwitch
{
    [CascadingParameter]
    private ContentTypeField? ContentTypeField { get; set; }

    [Parameter]
    public bool Value { get; set; } = default!;

    private BooleanFieldModel? FieldModel { get; set; }

    protected override Task OnInitializedAsync()
    {
        if (ContentTypeField is not null)
            FieldModel = ContentTypeField.ToFieldModel<BooleanFieldModel>();

        if (FieldModel.DefaultValue != null)
            Value = FieldModel.DefaultValue;

        return base.OnInitializedAsync();
    }
}
