namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public partial class NumberFieldFormRange
{
    [CascadingParameter]
    private ContentTypeField? ContentTypeField { get; set; }


    [Parameter]
    public decimal? Value { get; set; }

    private NumberFieldModel? FieldModel { get; set; }

    protected override Task OnInitializedAsync()
    {
        if (ContentTypeField is not null)
            FieldModel = ContentTypeField.ToFieldModel<NumberFieldModel>();

        if (FieldModel.DefaultValue != null)
            Value = FieldModel.DefaultValue;

        return base.OnInitializedAsync();
    }
}
