namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public partial class NumberFieldFormInput
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

        return base.OnInitializedAsync();
    }
}
